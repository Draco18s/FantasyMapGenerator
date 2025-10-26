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
		public ContinentalElevationRampFactors elevationRamping = ContinentalElevationRampFactors.nil;
		public bool encourageTileability;
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

		public GenParams()
		{
			seed = 0;
			encourageTileability = false;
			chunkExtents = default;
			temp_bias = 0;
			wet_bias = 0;
			sea_level = 0;
			global_modifier = 0;
			resolution = 0;
			fluxCapPercentile = 0;
			erosionRiverFactor = 0;
			erosionCreepFactor = 0;
			maxErosionRate = 0;
			ersionStrength = 0;
			riverFluxThreshold = 0;
			lakeFillThreshold = 0;
		}
	}

	/// <summary>
	/// Scales elevation towards zero the further from 0,0
	/// Radius is in pixel coordinates.
	/// Ramp factor is how quickly the elevation drops, in multiples of the radius. Negative values cause elevation to rise to maximum instead.
	/// </summary>
	public struct ContinentalElevationRampFactors
	{
		public static ContinentalElevationRampFactors nil = new ContinentalElevationRampFactors()
		{
			minRadius = 0,
			rampFactor = 1
		};
		public double minRadius;
		public double rampFactor;
	}
}
