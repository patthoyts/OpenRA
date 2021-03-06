﻿#region Copyright & License Information
/*
 * Copyright 2007-2010 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made 
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see LICENSE.
 */
#endregion

using OpenRA.Traits;

namespace OpenRA.Mods.RA
{
	public class SonarPulsePowerInfo : SupportPowerInfo
	{
		public override object Create(ActorInitializer init) { return new SonarPulsePower(init.self, this); }
	}

	public class SonarPulsePower : SupportPower
	{
		public SonarPulsePower(Actor self, SonarPulsePowerInfo info) : base(self, info) { }
		public override void Activate(Actor self, Order order)
		{
			// TODO: Reveal submarines

			// Should this play for all players?
			Sound.Play("sonpulse.aud");
		}
	}
}
