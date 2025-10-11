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
			mapGenerator = new WorldMapGenerator(new GenParams
			{
				chunkExtents = new Extents2d(0, 0, 256, 256),
				sea_level = 0.15,
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
				lakeFillThreshold = lakeThreshold
			});
			Console.WriteLine($"Generating chunk {timer.Elapsed}");
			MapChunk chunk = mapGenerator.GenerateChunk(0, 0);
			Console.WriteLine($"Rendering bitmap {timer.Elapsed}");
			Bitmap r = HeightMapGenerator.HeightMapRenderer(chunk, new Extents2d(0, 0, 256, 256), GetColor, IsLake);
			Console.WriteLine($"Rendering complete {timer.Elapsed}");

			string dir = Directory.GetCurrentDirectory();
			Console.WriteLine($"Saving to {dir}");
			string file = "map_01.png";
			r.Save(Path.Combine(dir, file), ImageFormat.Png);

			Console.WriteLine($"Generating chunk {timer.Elapsed}");
			chunk = mapGenerator.GenerateChunk(1, 0);
			Console.WriteLine($"Rendering bitmap {timer.Elapsed}");
			r = HeightMapGenerator.HeightMapRenderer(chunk, new Extents2d(0, 0, 256, 256), GetColor, IsLake);
			Console.WriteLine($"Rendering complete {timer.Elapsed}");

			dir = Directory.GetCurrentDirectory();
			Console.WriteLine($"Saving to {dir}");
			file = "map_02.png";
			r.Save(Path.Combine(dir, file), ImageFormat.Png);

			Console.WriteLine($"Generating chunk {timer.Elapsed}");
			chunk = mapGenerator.GenerateChunk(2, 0);
			Console.WriteLine($"Rendering bitmap {timer.Elapsed}");
			r = HeightMapGenerator.HeightMapRenderer(chunk, new Extents2d(0, 0, 256, 256), GetColor, IsLake);
			Console.WriteLine($"Rendering complete {timer.Elapsed}");

			dir = Directory.GetCurrentDirectory();
			Console.WriteLine($"Saving to {dir}");
			file = "map_03.png";
			r.Save(Path.Combine(dir, file), ImageFormat.Png);
		}

		private static bool IsLake(int bID, double depressionFillAmount)
		{
			if (bID >= 12)
			{
				if (depressionFillAmount < lakeThreshold / 1.375)
					bID -= 12;
			}
			else if (bID > 0)
			{
				if (depressionFillAmount >= lakeThreshold * 0.95)
				{
					bID += 12;
				}
			}
			return bID >= 12 || bID == 0;
		}

		public static Color GetColor(int bID, double depressionFillAmount)
		{
			Color c = GetBaseBiomeColor(bID);
			if (bID >= 12)
			{
				if (depressionFillAmount > lakeThreshold / 1.375)
					c = GetBaseBiomeColor(12);
			}
			else if (bID > 0)
			{
				if (depressionFillAmount >= lakeThreshold * 0.95)
				{
					c = GetBaseBiomeColor(12);
				}
			}
			return c;
		}

		private static Color GetBaseBiomeColor(int bID)
		{
			switch ((WorldMapGenerator.BiomeDef)bID)
			{
				case WorldMapGenerator.BiomeDef.Ocean:
					return Color.FromArgb(68, 107, 178);
				case WorldMapGenerator.BiomeDef.Grass:
					return Color.FromArgb(148, 184, 91);
				case WorldMapGenerator.BiomeDef.Desert:
					return Color.FromArgb(232, 212, 144);
				case WorldMapGenerator.BiomeDef.Savanna:
					return Color.FromArgb(191, 177, 100);
				case WorldMapGenerator.BiomeDef.Rainforest:
					return Color.FromArgb(82, 135, 71);
				case WorldMapGenerator.BiomeDef.Forest:
					return Color.FromArgb(103, 150, 89);
				case WorldMapGenerator.BiomeDef.Temperate:
					return Color.FromArgb(70, 122, 94);
				case WorldMapGenerator.BiomeDef.Tundra:
					return Color.FromArgb(189, 204, 196);
				case WorldMapGenerator.BiomeDef.Taiga:
					return Color.FromArgb(118, 140, 110);
				case WorldMapGenerator.BiomeDef.Mountain:
					return Color.FromArgb(158, 160, 163);
				case WorldMapGenerator.BiomeDef.SnowIce:
					return Color.FromArgb(240, 240, 240);
				case WorldMapGenerator.BiomeDef.River:
					return Color.FromArgb(68, 107, 178);
				case WorldMapGenerator.BiomeDef.Lake:
					return Color.FromArgb(68, 107, 178);
				default:
					return GetBaseBiomeColor(bID - 12);
			}
		}
	}
}