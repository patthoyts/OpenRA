#region Copyright & License Information
/*
 * Copyright 2007,2009,2010 Chris Forbes, Robert Pepperell, Matthew Bowra-Dean, Paul Chote, Alli Witheford.
 * This file is part of OpenRA.
 * 
 *  OpenRA is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 * 
 *  OpenRA is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 * 
 *  You should have received a copy of the GNU General Public License
 *  along with OpenRA.  If not, see <http://www.gnu.org/licenses/>.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.GameRules;
using OpenRA.Traits;

namespace OpenRA.Mods.RA
{
	public class WallInfo : ITraitInfo
	{
		public readonly UnitMovementType[] CrushableBy = { };

		public object Create(Actor self) { return new Wall(self); }
	}

	public class Wall : ICrushable, IOccupySpace, IBlocksBullets
	{
		readonly Actor self;
		public Wall(Actor self)
		{
			this.self = self;
			self.World.WorldActor.traits.Get<UnitInfluence>().Add(self, this);
		}

		public IEnumerable<int2> OccupiedCells() { yield return self.Location; }

		public void OnCrush(Actor crusher) { self.InflictDamage(crusher, self.Health, null); }
		public bool IsCrushableBy(UnitMovementType umt, Player player)
		{
			return self.Info.Traits.Get<WallInfo>().CrushableBy.Contains(umt);
		}

		public bool IsPathableCrush(UnitMovementType umt, Player player)
		{
			return IsCrushableBy(umt, player);
		}
	}
}