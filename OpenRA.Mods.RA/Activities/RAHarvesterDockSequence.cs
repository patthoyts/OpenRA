#region Copyright & License Information
/*
 * Copyright 2007-2010 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made 
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see LICENSE.
 */
#endregion

using OpenRA.Mods.RA.Activities;
using OpenRA.Traits;
using OpenRA.Traits.Activities;
using System.Collections.Generic;
using OpenRA.Mods.RA.Move;
using OpenRA.Mods.RA.Render;
using System;

namespace OpenRA.Mods.RA
{
	public class RAHarvesterDockSequence : IActivity
	{
		enum State
		{
			Wait,
			Turn,
			Dock,
			Loop,
			Undock,
			Complete
		};
		
		readonly Actor proc;
		readonly Harvester harv;
		readonly RenderUnit ru;
		State state;

		public RAHarvesterDockSequence(Actor self, Actor proc)
		{
			this.proc = proc;
			state = State.Turn;
			harv = self.Trait<Harvester>();
			ru = self.Trait<RenderUnit>();
		}
		
		IActivity NextActivity { get; set; }
                        
		public IActivity Tick(Actor self)
		{
			switch (state)
			{
				case State.Wait:
					return this;
				case State.Turn:
					state = State.Dock;
					return Util.SequenceActivities(new Turn(64), this);
				case State.Dock:
					ru.PlayCustomAnimation(self, "dock", () => {ru.PlayCustomAnimRepeating(self, "dock-loop"); state = State.Loop;});
					state = State.Wait;
					return this;
				case State.Loop:
					if (harv.TickUnload(self, proc))
						state = State.Undock;
					return this;
				case State.Undock:
					ru.PlayCustomAnimBackwards(self, "dock", () => state = State.Complete);
					state = State.Wait;
					return this;
				case State.Complete:
					return NextActivity;
			}
			throw new InvalidOperationException("Invalid harvester dock state");
		}

		public void Cancel(Actor self)
		{
			state = State.Undock;
		}

		public void Queue( IActivity activity )
		{
			if( NextActivity != null )
				NextActivity.Queue( activity );
			else
				NextActivity = activity;
		}

		public IEnumerable<float2> GetCurrentPath()
		{
			yield break;
		}
	}
}

