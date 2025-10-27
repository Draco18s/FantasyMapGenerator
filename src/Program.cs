// See https://aka.ms/new-console-template for more information

using System.Diagnostics;
using OLearyMapGen.math;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net.Mime;

namespace OLearyMapGen
{
	class Program
	{
		public static Stopwatch timer;
		private static WorldMapGenerator mapGenerator;
		private const double lakeThreshold = 0.025;
		static void Main(string[] args)
		{
			Console.WriteLine("Initializing");
			timer = new Stopwatch();
			timer.Start();
			Extents2d extents = new Extents2d(0, 0, 256, 256);
			mapGenerator = new WorldMapGenerator(new GenParams
			{
				encourageTileability = true,
				chunkExtents = extents,
				sea_level = 0.3,
				temp_bias = 0.0,
				wet_bias = 0.0,
				seed = 56874645123,
				resolution = 8,
				fluxCapPercentile = 0.995,
				global_modifier = 0.4,
				erosionCreepFactor = 500,
				maxErosionRate = 50,
				erosionRiverFactor = 500,
				ersionStrength = 0.1,
				riverFluxThreshold = 0.06,
				lakeFillThreshold = lakeThreshold,
				elevationRamping = new ContinentalElevationRampFactors()
				{
					minRadius = 192,
					rampFactor = -0.3
				}
			});
			Console.WriteLine($"Generating chunk {timer.Elapsed}");
			MapChunk chunk = mapGenerator.GenerateChunk(0, -1);
			Console.WriteLine($"Rendering bitmap {timer.Elapsed}");
			Bitmap r = HeightMapGenerator.HeightMapRenderer(chunk, extents, GetColor, IsLake);
			Console.WriteLine($"Rendering complete {timer.Elapsed}");

			string dir = Directory.GetCurrentDirectory();
			Console.WriteLine($"Saving to {dir}");
			string file = "map_01.png";
			r.Save(Path.Combine(dir, file), ImageFormat.Png);

			Console.WriteLine($"Generating chunk {timer.Elapsed}");
			chunk = mapGenerator.GenerateChunk(1, -1);
			Console.WriteLine($"Rendering bitmap {timer.Elapsed}");
			r = HeightMapGenerator.HeightMapRenderer(chunk, extents, GetColor, IsLake);
			Console.WriteLine($"Rendering complete {timer.Elapsed}");

			dir = Directory.GetCurrentDirectory();
			Console.WriteLine($"Saving to {dir}");
			file = "map_02.png";
			r.Save(Path.Combine(dir, file), ImageFormat.Png);

			Console.WriteLine($"Generating chunk {timer.Elapsed}");
			chunk = mapGenerator.GenerateChunk(2, -1);
			Console.WriteLine($"Rendering bitmap {timer.Elapsed}");
			r = HeightMapGenerator.HeightMapRenderer(chunk, extents, GetColor, IsLake);
			Console.WriteLine($"Rendering complete {timer.Elapsed}");

			dir = Directory.GetCurrentDirectory();
			Console.WriteLine($"Saving to {dir}");
			file = "map_03.png";
			r.Save(Path.Combine(dir, file), ImageFormat.Png);
		}

		private static bool IsLake(BiomeDef bID, double depressionFillAmount)
		{
			if (bID >= BiomeDef.Lake)
			{
				if (depressionFillAmount < lakeThreshold / 1.375)
					bID -= (int)BiomeDef.Lake;
			}
			else if (bID > (int)BiomeDef.Ocean)
			{
				if (depressionFillAmount >= lakeThreshold * 0.95)
				{
					bID += (int)BiomeDef.Lake;
				}
			}
			return bID >= BiomeDef.Lake || bID == BiomeDef.Ocean;
		}

		public static Color GetColor(BiomeDef bID, double depressionFillAmount)
		{
			Color c = GetBaseBiomeColor(bID);
			if (bID >= BiomeDef.Lake)
			{
				if (depressionFillAmount > lakeThreshold / 1.375)
					c = GetBaseBiomeColor(BiomeDef.Lake);
			}
			else if (bID > BiomeDef.Ocean)
			{
				if (depressionFillAmount >= lakeThreshold * 0.95)
				{
					c = GetBaseBiomeColor(BiomeDef.Lake);
				}
			}
			return c;
		}

		private static Color GetBaseBiomeColor(BiomeDef bID)
		{
			switch (bID)
			{
				case BiomeDef.Ocean:
					return Color.FromArgb(68, 107, 178);
				case BiomeDef.Plains:
					return Color.FromArgb(148, 184, 91);
				case BiomeDef.Desert:
					return Color.FromArgb(232, 212, 144);
				case BiomeDef.ColdDesert:
					return Color.FromArgb(224, 204, 140);
				case BiomeDef.IcePlains:
					return Color.FromArgb(200, 220, 240);
				case BiomeDef.Beach:
					return Color.FromArgb(245, 245, 132);
				case BiomeDef.Badlands:
					return Color.FromArgb(245, 204, 10);
				case BiomeDef.Savanna:
					return Color.FromArgb(191, 177, 100);
				case BiomeDef.Rainforest:
					return Color.FromArgb(82, 135, 71);
				case BiomeDef.SeasonForest:
					return Color.FromArgb(103, 150, 89);
				case BiomeDef.TropRainforest:
					return Color.FromArgb(70, 122, 94);
				case BiomeDef.PineForest:
					return Color.FromArgb(0, 84, 0);
				case BiomeDef.Tundra:
					return Color.FromArgb(189, 204, 196);
				case BiomeDef.Taiga:
					return Color.FromArgb(118, 140, 110);
				case BiomeDef.Mountain:
					return Color.FromArgb(158, 160, 163);
				case BiomeDef.Snow:
					return Color.FromArgb(240, 240, 240);
				case BiomeDef.River:
					return Color.FromArgb(68, 107, 178);
				case BiomeDef.Lake:
					return Color.FromArgb(68, 107, 178);
				case BiomeDef.Swamp:
					return Color.FromArgb(75, 75, 15);
				case BiomeDef.RockShore:
					return Color.FromArgb(70, 85, 95);
				case BiomeDef.Shrubland:
					return Color.FromArgb(120, 90, 10);
				default:
					return GetBaseBiomeColor(bID - (int)BiomeDef.Lake);
			}
		}
	}
}