// See https://aka.ms/new-console-template for more information
namespace OLearyMapGen
{
	class Program
	{
		private static WorldMapGenerator mapGenerator;
		static void Main(string[] args)
		{
			Console.WriteLine("Initializing");
			mapGenerator = new WorldMapGenerator(new WorldMapGenerator.GenParams()
			{
				chunk_width = 256,
				chunk_height = 256,
				sea_level = 0.35,
				temp_bias = 0.0,
				wet_bias = 0.0,
				seed = 84648456,
				resolution = 512,
				global_modifier = 0.5
			});

			mapGenerator.GenerateChunk(0,0);
		}
	}
}