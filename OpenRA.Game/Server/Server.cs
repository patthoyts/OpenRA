#region Copyright & License Information
/*
 * Copyright 2007-2010 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made 
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see LICENSE.
 */
#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using OpenRA.FileFormats;
using OpenRA.GameRules;
using OpenRA.Network;

namespace OpenRA.Server
{
	public class Server
	{
		// Valid player connections
		public List<Connection> conns = new List<Connection>();
		
		// Pre-verified player connections
		public List<Connection> preConns = new List<Connection>();

		TcpListener mListener = null;
		TcpListener mListener6 = null;
		Dictionary<int, List<Connection>> inFlightFrames
			= new Dictionary<int, List<Connection>>();
		
		TypeDictionary ServerTraits = new TypeDictionary();
		public Session lobbyInfo;
		public bool GameStarted = false;
		public string Name;
		int randomSeed;

		public ModData ModData;
		public Map Map;

		bool shutdown = false;
		public void Shutdown()
		{
			shutdown = true;
		}
		
		public Server(ModData modData, Settings settings, string map)
		{
			Log.AddChannel("server", "server.log");

			mListener = new TcpListener(IPAddress.Any, settings.Server.ListenPort);
			mListener6 = new TcpListener(IPAddress.IPv6Any, settings.Server.ListenPort);
			Name = settings.Server.Name;
			randomSeed = (int)DateTime.Now.ToBinary();
			ModData = modData;

			foreach (var trait in modData.Manifest.ServerTraits)
				ServerTraits.Add( modData.ObjectCreator.CreateObject<ServerTrait>(trait) );
			
			lobbyInfo = new Session( settings.Game.Mods );
			lobbyInfo.GlobalSettings.RandomSeed = randomSeed;
			lobbyInfo.GlobalSettings.Map = map;
			lobbyInfo.GlobalSettings.AllowCheats = settings.Server.AllowCheats;
			lobbyInfo.GlobalSettings.ServerName = settings.Server.Name;
			
			foreach (var t in ServerTraits.WithInterface<INotifyServerStart>())
				t.ServerStarted(this);
						
			Log.Write("server", "Initial mods: ");
			foreach( var m in lobbyInfo.GlobalSettings.Mods )
				Log.Write("server","- {0}", m);
			
			Log.Write("server", "Initial map: {0}",lobbyInfo.GlobalSettings.Map);

			try { mListener.Start(); } catch (Exception e) { Log.Write("server", "problem: " + e.Message); mListener = null; }
			try { mListener6.Start(); } catch (Exception e) { Log.Write("server", "problem: " + e.Message); mListener6 = null; }

			if (mListener == null && mListener6 == null)
			{
				throw new InvalidOperationException( "Unable to start server: port is already in use" );
			}

			new Thread( _ =>
			{
				var timeout = ServerTraits.WithInterface<ITick>().Min(t => t.TickTimeout);
				for( ; ; )
				{
					var checkRead = new ArrayList();
					if (mListener != null) checkRead.Add( mListener.Server );
					if (mListener6 != null) checkRead.Add( mListener6.Server );
					foreach (var c in conns) checkRead.Add(c.socket);
					foreach( var c in preConns ) checkRead.Add( c.socket );
					
					Socket.Select( checkRead, null, null, timeout );

					foreach( Socket s in checkRead )
						if( mListener != null && s == mListener.Server )
							AcceptConnection(mListener);
						else if( mListener6 != null && s == mListener6.Server )
							AcceptConnection(mListener6);
						else if (preConns.Count > 0)
						{
							var p = preConns.SingleOrDefault( c => c.socket == s );
							if (p != null) p.ReadData( this );
						}
						else if (conns.Count > 0) conns.Single( c => c.socket == s ).ReadData( this );

					foreach (var t in ServerTraits.WithInterface<ITick>())
						t.Tick(this);
					
					if (shutdown)
						break;
				}
				
				GameStarted = false;
				foreach (var t in ServerTraits.WithInterface<INotifyServerShutdown>())
					t.ServerShutdown(this);

				preConns.Clear();
				conns.Clear();
				try { mListener.Stop(); } catch { }
				try { mListener6.Stop(); } catch { }
			} ) { IsBackground = true }.Start();
		}

		/* lobby rework todo: 
		 *	- "teams together" option for team games -- will eliminate most need
		 *		for manual spawnpoint choosing.
		 *	- 256 max players is a dirty hack
		 */
		int ChooseFreePlayerIndex()
		{
			for (var i = 0; i < 256; i++)
				if (conns.All(c => c.PlayerIndex != i) && preConns.All(c => c.PlayerIndex != i))
					return i;

			throw new InvalidOperationException("Already got 256 players");
		}

		void AcceptConnection(TcpListener listener)
		{
			Socket newSocket = null;

			try
			{
				if (!listener.Server.IsBound) return;
				newSocket = listener.AcceptSocket();
			}
			catch
			{
				/* could have an exception here when listener 'goes away' when calling AcceptConnection! */
				/* alternative would be to use locking but the listener doesnt go away without a reason */
				return; 
			}

			var newConn = new Connection { socket = newSocket };
			try
			{
				newConn.socket.Blocking = false;
				newConn.socket.NoDelay = true;

				// assign the player number.
				newConn.PlayerIndex = ChooseFreePlayerIndex();
				newConn.socket.Send(BitConverter.GetBytes(ProtocolVersion.Version));
				newConn.socket.Send(BitConverter.GetBytes(newConn.PlayerIndex));
				preConns.Add(newConn);
				
				// Dispatch a handshake order
				var request = new HandshakeRequest()
				{
					Map = lobbyInfo.GlobalSettings.Map,
					Mods = lobbyInfo.GlobalSettings.Mods.Select(m => "{0}@{1}".F(m,Mod.AllMods[m].Version)).ToArray()
				};
				DispatchOrdersToClient(newConn, 0, 0, new ServerOrder("HandshakeRequest", request.Serialize()).Serialize());
			}
			catch (Exception) { DropClient(newConn); }
		}
		
		void ValidateClient(Connection newConn, string data)
		{
			try
			{
				if (GameStarted)
				{
					Log.Write("server", "Rejected connection from {0}; game is already started.", 
						newConn.socket.RemoteEndPoint);
					
					SendOrderTo(newConn, "ServerError", "The game has already started");
					DropClient(newConn);
					return;
				}
				
				var handshake = HandshakeResponse.Deserialize(data);
				var client = handshake.Client;
				var mods = handshake.Mods;
				
				// Check that the client has compatable mods
				var valid = mods.All( m => m.Contains('@')) && //valid format
				            mods.Count() == Game.CurrentMods.Count() &&  //same number
				            mods.Select( m => Pair.New(m.Split('@')[0], m.Split('@')[1])).All(kv => Game.CurrentMods.ContainsKey(kv.First) &&
				     		(kv.Second == "{DEV_VERSION}" || Game.CurrentMods[kv.First].Version == "{DEV_VERSION}" || kv.Second == Game.CurrentMods[kv.First].Version));
				if (!valid) 
				{
					Log.Write("server", "Rejected connection from {0}; mods do not match.", 
						newConn.socket.RemoteEndPoint);
					
					SendOrderTo(newConn, "ServerError", "Your mods don't match the server");
					DropClient(newConn);
					return;
				}
				
				// Promote connection to a valid client
				preConns.Remove(newConn);
				conns.Add(newConn);

				// Enforce correct PlayerIndex and Slot
				client.Index = newConn.PlayerIndex;
				client.Slot = ChooseFreeSlot();
				
				var slotData = lobbyInfo.Slots.FirstOrDefault( x => x.Index == client.Slot );
				if (slotData != null && slotData.MapPlayer != null)
					SyncClientToPlayerReference(client, Map.Players[slotData.MapPlayer]);
				
				lobbyInfo.Clients.Add(client);
	
				Log.Write("server", "Client {0}: Accepted connection from {1}",
					newConn.PlayerIndex, newConn.socket.RemoteEndPoint);
				SendChat(newConn, "has joined the game.");
				
				foreach (var t in ServerTraits.WithInterface<IClientJoined>())
					t.ClientJoined(this, newConn);
				
				SyncLobbyInfo();
			}
			catch (Exception) { DropClient(newConn); }
		}

		int ChooseFreeSlot()
		{
			return lobbyInfo.Slots.First(s => !s.Closed && s.Bot == null 
				&& !lobbyInfo.Clients.Any( c => c.Slot == s.Index )).Index;
		}
		
		
		public static void SyncClientToPlayerReference(Session.Client c, PlayerReference pr)
		{
			if (pr == null)
				return;
			if (pr.LockColor)
                c.ColorRamp = pr.ColorRamp;
			if (pr.LockRace)
				c.Country = pr.Race;
		}
		
		public void UpdateInFlightFrames(Connection conn)
		{
			if (conn.Frame == 0)
				return;

			if (!inFlightFrames.ContainsKey(conn.Frame))
				inFlightFrames[conn.Frame] = new List<Connection> { conn };
			else
				inFlightFrames[conn.Frame].Add(conn);

			if (conns.All(c => inFlightFrames[conn.Frame].Contains(c)))
				inFlightFrames.Remove(conn.Frame);
		}

		void DispatchOrdersToClient(Connection c, int client, int frame, byte[] data)
		{
			try
			{
				var ms = new MemoryStream();
				ms.Write( BitConverter.GetBytes( data.Length + 4 ) );
				ms.Write( BitConverter.GetBytes( client ) );
				ms.Write( BitConverter.GetBytes( frame ) );
				ms.Write( data );
				c.socket.Send( ms.ToArray() );
			}
			catch (Exception) { DropClient(c); }
		}

		public void DispatchOrders(Connection conn, int frame, byte[] data)
		{
			if (frame == 0 && conn != null)
				InterpretServerOrders(conn, data);
			else
			{
				var from = conn != null ? conn.PlayerIndex : 0;
				foreach (var c in conns.Except(conn).ToArray())
					DispatchOrdersToClient(c, from, frame, data);
			}
		}

		void InterpretServerOrders(Connection conn, byte[] data)
		{
			var ms = new MemoryStream(data);
			var br = new BinaryReader(ms);

			try
			{
				for (; ; )
				{
					var so = ServerOrder.Deserialize(br);
					if (so == null) return;
					InterpretServerOrder(conn, so);
				}
			}
			catch (EndOfStreamException) { }
			catch (NotImplementedException) { }
		}
		
		public void SendChatTo(Connection conn, string text)
		{
			SendOrderTo(conn, "Chat", text);
		}

		public void SendOrderTo(Connection conn, string order, string data)
		{
			DispatchOrdersToClient(conn, 0, 0,
				new ServerOrder(order, data).Serialize());
		}
		
        public void SendChat(Connection asConn, string text)
        {
            DispatchOrders(asConn, 0, new ServerOrder("Chat", text).Serialize());
        }

        public void SendDisconnected(Connection asConn)
        {
            DispatchOrders(asConn, 0, new ServerOrder("Disconnected", "").Serialize());
        }

		void InterpretServerOrder(Connection conn, ServerOrder so)
		{
			switch (so.Name)
			{
				case "Command":
					bool handled = false;
					foreach (var t in ServerTraits.WithInterface<IInterpretCommand>())
						if ((handled = t.InterpretCommand(this, conn, GetClient(conn), so.Data)))
							break;
					
					if (!handled)
					{
						Log.Write("server", "Unknown server command: {0}", so.Data);
						SendChatTo(conn, "Unknown server command: {0}".F(so.Data));
					}
				
					break;
				case "HandshakeResponse":
					ValidateClient(conn, so.Data);					
					break;
				case "Chat":
				case "TeamChat":
					var fromClient = GetClient(conn);
					var fromIndex = fromClient != null ? fromClient.Index : 0;

					foreach (var c in conns.Except(conn).ToArray())
						DispatchOrdersToClient(c, fromIndex, 0, so.Serialize());
				break;
			}
		}

		public Session.Client GetClient(Connection conn)
		{
			return lobbyInfo.ClientWithIndex(conn.PlayerIndex);
		}

		public void DropClient(Connection toDrop)
		{
			if (preConns.Contains(toDrop))
				preConns.Remove(toDrop);
			else
			{
				conns.Remove(toDrop);
				SendChat(toDrop, "Connection Dropped");

            	if (GameStarted)
                	SendDisconnected(toDrop); /* Report disconnection */

				lobbyInfo.Clients.RemoveAll(c => c.Index == toDrop.PlayerIndex);
				
				DispatchOrders( toDrop, toDrop.MostRecentFrame, new byte[] { 0xbf } );

				if (conns.Count != 0)
					SyncLobbyInfo();
			}			

			try
			{
				toDrop.socket.Disconnect(false);
			}
			catch { }
		}

		public void SyncLobbyInfo()
		{
			if (!GameStarted)	/* don't do this while the game is running, it breaks things. */
				DispatchOrders(null, 0,
					new ServerOrder("SyncInfo", lobbyInfo.Serialize()).Serialize());

			foreach (var t in ServerTraits.WithInterface<INotifySyncLobbyInfo>())
				t.LobbyInfoSynced(this);
		}
		
		public void StartGame()
		{
			GameStarted = true;
			foreach( var c in conns )
				foreach( var d in conns )
					DispatchOrdersToClient( c, d.PlayerIndex, 0x7FFFFFFF, new byte[] { 0xBF } );

			// Drop any unvalidated clients
			foreach (var c in preConns)
				DropClient(c);
			
			DispatchOrders(null, 0,
				new ServerOrder("StartGame", "").Serialize());

			foreach (var t in ServerTraits.WithInterface<IStartGame>())
				t.GameStarted(this);
		}
	}
}
