using OLearyMapGen.math;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OLearyMapGen
{
	public struct GenParams
	{
		public long seed;
		public Extents2d chunkExtents;
		public double temp_bias;
		public double wet_bias;
		public double sea_level;
		public double global_modifier;
		public double resolution;
		public double fluxCapPercentile;
		public double erosionRiverFactor;
		public double erosionCreepFactor;
		public double maxErosionRate;
		public double ersionStrength;
		public double riverFluxThreshold;
		public double lakeFillThreshold;
	}
}
