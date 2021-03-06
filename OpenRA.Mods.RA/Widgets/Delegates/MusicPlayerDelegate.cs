#region Copyright & License Information
/*
 * Copyright 2007-2010 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made 
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see LICENSE.
 */
#endregion

using System.Drawing;
using System.Linq;
using OpenRA.FileFormats;
using OpenRA.Support;
using OpenRA.Widgets;

namespace OpenRA.Mods.RA.Widgets.Delegates
{
	public class MusicPlayerDelegate : IWidgetDelegate
	{
		string CurrentSong = null;
		public MusicPlayerDelegate()
		{
			var bg = Widget.RootWidget.GetWidget("MUSIC_MENU");
			CurrentSong = GetNextSong();

			bg.GetWidget("BUTTON_CLOSE").OnMouseUp = mi => {
				Game.Settings.Save();
				Widget.CloseWindow();
				return true;
			};

			bg.GetWidget("BUTTON_PLAY").OnMouseUp = mi =>
			{
				if (CurrentSong == null)
					return true;
				
				Sound.PlayMusicThen(Rules.Music[CurrentSong].Filename,
				      () => bg.GetWidget(Game.Settings.Sound.Repeat ? "BUTTON_PLAY" : "BUTTON_NEXT").OnMouseUp(new MouseInput()));
				bg.GetWidget("BUTTON_PLAY").Visible = false;
				bg.GetWidget("BUTTON_PAUSE").Visible = true;

				return true;
			};
			
			bg.GetWidget("BUTTON_PAUSE").OnMouseUp = mi =>
			{				
				Sound.PauseMusic();
				bg.GetWidget("BUTTON_PAUSE").Visible = false;
				bg.GetWidget("BUTTON_PLAY").Visible = true;
				return true;
			};
			
			bg.GetWidget("BUTTON_STOP").OnMouseUp = mi =>
			{
				Sound.StopMusic();
				bg.GetWidget("BUTTON_PAUSE").Visible = false;
				bg.GetWidget("BUTTON_PLAY").Visible = true;
				
				return true;
			};
			
			bg.GetWidget("BUTTON_NEXT").OnMouseUp = mi =>
			{
				CurrentSong = GetNextSong();
				return bg.GetWidget("BUTTON_PLAY").OnMouseUp(mi);
			};

			bg.GetWidget("BUTTON_PREV").OnMouseUp = mi =>
			{
				CurrentSong = GetPrevSong();
				return bg.GetWidget("BUTTON_PLAY").OnMouseUp(mi);
			};
			
			bg.GetWidget<CheckboxWidget>("SHUFFLE").Bind(Game.Settings.Sound, "Shuffle");
			bg.GetWidget<CheckboxWidget>("REPEAT").Bind(Game.Settings.Sound, "Repeat");

			bg.GetWidget<LabelWidget>("TIME").GetText = () =>
			{
				if (CurrentSong == null)
					return "";
				return "{0:D2}:{1:D2} / {2:D2}:{3:D2}".F((int)Sound.MusicSeekPosition / 60, (int)Sound.MusicSeekPosition % 60,
			                                                                                    Rules.Music[CurrentSong].Length / 60, Rules.Music[CurrentSong].Length % 60);
			};
			
			var ml = bg.GetWidget<ScrollPanelWidget>("MUSIC_LIST");
			var itemTemplate = ml.GetWidget<LabelWidget>("MUSIC_TEMPLATE");
			
			if (!Rules.Music.Where(m => m.Value.Exists).Any())
			{
				itemTemplate.IsVisible = () => true;
				itemTemplate.GetWidget<LabelWidget>("TITLE").GetText = () => "No Music Installed";
				itemTemplate.GetWidget<LabelWidget>("TITLE").Align = LabelWidget.TextAlign.Center;
			}
			
			foreach (var kv in Rules.Music.Where(m => m.Value.Exists))
			{
				var song = kv.Key;
				if (CurrentSong == null)
					CurrentSong = song;

				var template = itemTemplate.Clone() as LabelWidget;
				template.Id = "SONG_{0}".F(song);
				template.GetBackground = () => ((song == CurrentSong) ? "dialog2" : null);
				template.OnMouseDown = mi =>
				{
					if (mi.Button != MouseButton.Left) return false;
					CurrentSong = song;
					bg.GetWidget("BUTTON_PLAY").OnMouseUp(mi);
					return true;
				};

				template.IsVisible = () => true;				
				template.GetWidget<LabelWidget>("TITLE").GetText = () => "   " + Rules.Music[song].Title;
				template.GetWidget<LabelWidget>("LENGTH").GetText = () => "{0:D1}:{1:D2}".F(Rules.Music[song].Length / 60, Rules.Music[song].Length % 60);

				ml.AddChild(template);
			}
		}
		
		string GetNextSong()
		{
			var songs = Rules.Music.Where(a => a.Value.Exists)
				.Select(a => a.Key);
			
			if (!songs.Any())
				return null;
			
			if (Game.Settings.Sound.Shuffle)
				return songs.Random(Game.CosmeticRandom);
			
			return songs.SkipWhile(m => m != CurrentSong)
				.Skip(1).FirstOrDefault() ?? songs.FirstOrDefault();

		}

		string GetPrevSong()
		{
			var songs = Rules.Music.Where(a => a.Value.Exists)
				.Select(a => a.Key).Reverse();
			
			if (!songs.Any())
				return null;
			
			if (Game.Settings.Sound.Shuffle)
				return songs.Random(Game.CosmeticRandom);
			
			return songs.SkipWhile(m => m != CurrentSong)
				.Skip(1).FirstOrDefault() ?? songs.FirstOrDefault();
		}
	}
}
