using OLearyMapGen.math;
using SharpVoronoiLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static System.Formats.Asn1.AsnWriter;

namespace OLearyMapGen
{
	public class WorldMapGenerator
	{
		public struct GenParams
		{
			public long seed;
			public float chunk_width;
			public float chunk_height;
			public double temp_bias;
			public double wet_bias;
			public double sea_level;
			public double global_modifier;
			public int resolution;
		}

		private NodeMap<double> _heightMap;
		private NodeMap<double> _tempMap;
		private NodeMap<double> _waterMap;
		private NodeMap<double> _biomeMap;
		private FastNoiseLite _noise;
		private GenParams _config;

		public WorldMapGenerator(GenParams config)
		{
			_config = config;
			VoronoiPlane plane = new VoronoiPlane(0, 0, _config.chunk_width, _config.chunk_height);

			UniformPoissonDiskSampler.SetSeed((uint)_config.seed);
			IEnumerable<VoronoiSite> pts = UniformPoissonDiskSampler.SampleRectangle(new Vector2(0, 0), new Vector2(_config.chunk_width, _config.chunk_height), 1, _config.resolution)
				.Select(p => new VoronoiSite(p.X, p.Y));
			plane.SetSites(pts.ToList());
			plane.Relax(5);
			_heightMap = new NodeMap<double>(new VertexMap(plane, new VertexMap.Extents2d(0, 0, _config.chunk_width, _config.chunk_height)), 0.0);
			_tempMap = new NodeMap<double>(new VertexMap(plane, new VertexMap.Extents2d(0, 0, _config.chunk_width, _config.chunk_height)), 0.0);
			_waterMap = new NodeMap<double>(new VertexMap(plane, new VertexMap.Extents2d(0, 0, _config.chunk_width, _config.chunk_height)), 0.0);
			_biomeMap = new NodeMap<double>(new VertexMap(plane, new VertexMap.Extents2d(0, 0, _config.chunk_width, _config.chunk_height)), 0.0);
			_noise = new FastNoiseLite(_config.seed);
			_noise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
		}

		public void Reset()
		{
			_heightMap.Fill(0);
			_tempMap.Fill(0);
			_waterMap.Fill(0);
			//UniformPoissonDiskSampler.SetSeed((uint)_config.seed);
		}

		public void GenerateChunk(int x, int y)
		{
			ElevationPass(x, y);
			TemperaturePass(x, y);
			MoisturePass(x, y);
		}

		private void MoisturePass(int x, int y)
		{
			for (int i = 0; i < _waterMap.Size(); i++)
			{
				VoronoiPoint v = _waterMap.GetVertex(i);
				double xx = v.X + x * _config.chunk_width;
				double yy = v.Y + y * _config.chunk_height;
				_waterMap.Set(i, MoistureAt(xx, yy));
			}
		}

		private double MoistureAt(double x, double y)
		{
			double moist_scale = 1024.0 * _config.global_modifier;
			double moist_noise = FbmNoise(x, y, moist_scale, 6, 0.5, 2.2, 10);
			double normalization_factor = 1.3;
			double moisture_map = Math.Clamp((moist_noise / normalization_factor + 1.0) / 2.0, 0, 1);
			moisture_map += _config.wet_bias;

			return Math.Clamp(moisture_map, 0, 1);
		}

		private void TemperaturePass(int x, int y)
		{
			for (int i = 0; i < _tempMap.Size(); i++)
			{
				VoronoiPoint v = _tempMap.GetVertex(i);
				double xx = v.X + x * _config.chunk_width;
				double yy = v.Y + y * _config.chunk_height;
				_tempMap.Set(i, TemperatureAt(xx, yy, v));
			}
		}

		private double TemperatureAt(double x, double y, VoronoiPoint v) 
		{
			double temp_scale = 1024.0 * _config.global_modifier;
			double temp_noise = FbmNoise(x, y, temp_scale, 5, 0.5, 2.1, 20);
			double normalization_factor = 1.2;
			double temp_map = Math.Clamp((temp_noise / normalization_factor + 1.0) / 2.0, 0, 1);
			temp_map += _config.temp_bias;

			double height_above_sea = _heightMap.Get(_heightMap.GetNodeIndex(v)) - _config.sea_level;
			double max_height_above_sea = 1.0 - _config.sea_level;
			if (max_height_above_sea > 0)
				height_above_sea = height_above_sea / max_height_above_sea;
			else
				height_above_sea = 0;

			double MAX_TEMP_DROP = 0.15;
			double altitude_modifier = Math.Pow(height_above_sea, 1.5) * MAX_TEMP_DROP;

			return Math.Clamp(temp_map - altitude_modifier, 0, 1);
		}

		private void ElevationPass(double x, double y)
		{
			for (int i = 0; i < _heightMap.Size(); i++)
			{
				VoronoiPoint v = _heightMap.GetVertex(i);
				double xx = v.X + x * _config.chunk_width;
				double yy = v.Y + y * _config.chunk_height;
				_heightMap.Set(1, ElevationAt(xx,yy));
			}

			_heightMap.SetLevelToMedian();
			_heightMap.Adjust(_config.sea_level);
		}

		private double ElevationAt(double x, double y)
		{

			double warp_scale_large = 2048.0 * _config.global_modifier;
			double warp_strength_large = warp_scale_large * 0.1;

			double warp_x_l = FbmNoise(x, y, warp_scale_large, 3, 0.5, 2.0, 1) * warp_strength_large;
			double warp_y_l = FbmNoise(x, y, warp_scale_large, 3, 0.5, 2.0, 2) * warp_strength_large;

			double warp_scale_medium = 512.0 * _config.global_modifier;
			double warp_strength_medium = warp_scale_medium * 0.5;

			double warp_x_m = FbmNoise(x, y, warp_scale_medium, 5, 0.5, 2.0, 3) * warp_strength_medium;
			double warp_y_m = FbmNoise(x, y, warp_scale_medium, 5, 0.5, 2.0, 4) * warp_strength_medium;

			double warped_x = x + warp_x_l + warp_x_m;
			double warped_y = y + warp_y_l + warp_y_m;

			double base_scale = 2048.0 * _config.global_modifier;
			double base_noise = FbmNoise(x, y, base_scale, 6, 0.5, 2.0, 0);

			double detail_scale = 512.0 * _config.global_modifier;
			double detail_noise = FractalNoise(warped_x, warped_y, detail_scale, 8, 0.5, 2.1, 10);
			double normalization_factor_detail = 1.1;
			detail_noise = Math.Clamp((detail_noise / normalization_factor_detail + 1.0) / 2.0, 0, 1);

			double mountain_scale = 512.0 * _config.global_modifier;
			double mountain_noise = RidgedFbmNoise(x, y, mountain_scale, 6, 0.45, 2.0, 20);

			double mask_scale = 1536.0 * _config.global_modifier;
			int mask_x = (int)Math.Round(x + warp_x_l);
			int mask_y = (int)Math.Round(y + warp_y_l);
			double mask_noise = RidgedFbmNoise(mask_x, mask_y, mask_scale, 4, 0.5, 2.0, 21);

			double mask_norm_factor = 1.1;
			double mountain_mask = Math.Pow(Math.Clamp((mask_noise / mask_norm_factor + 1.0) / 2.0 - 0.5, 0, 0.5)+0.5,3);

			double detail_contribution = detail_noise * 0.3;
			double elevation = base_noise + detail_contribution;
			double mountain_contribution = mountain_noise * mountain_mask * 0.5;
			elevation += mountain_contribution;

			elevation = (Math.Tan((elevation * 2.4) - 1.2 + 0.35) + 0.35)/ 3.75;

			return Math.Clamp(elevation,0,1);
		}

		private double FractalNoise(double x, double y, double scale, int octaves, double persistence, double lacunarity, int seed_offset = 0)
		{
			_noise.SetFractalType(FastNoiseLite.FractalType.None);
			_noise.SetSeed(_config.seed + seed_offset);
			_noise.SetFractalLacunarity((float)lacunarity);
			_noise.SetFractalOctaves(octaves);
			_noise.SetFractalPersistence((float)persistence);
			_noise.SetFrequency((float)(1.0 / scale));

			return _noise.GetNoise((float)x, (float)y);
		}

		private double FbmNoise(double x, double y, double scale, int octaves, double persistence, double lacunarity, int seed_offset = 0)
		{
			_noise.SetFractalType(FastNoiseLite.FractalType.FBm);
			_noise.SetSeed(_config.seed + seed_offset);
			_noise.SetFractalLacunarity((float)lacunarity);
			_noise.SetFractalOctaves(octaves);
			_noise.SetFractalPersistence((float)persistence);
			_noise.SetFrequency((float)(1.0 / scale));

			return _noise.GetNoise((float)x, (float)y);
		}

		private double RidgedFbmNoise(double x, double y, double scale, int octaves, double persistence, double lacunarity, int seed_offset = 0)
		{
			_noise.SetFractalType(FastNoiseLite.FractalType.Ridged);
			_noise.SetSeed(_config.seed + seed_offset);
			_noise.SetFractalLacunarity((float)lacunarity);
			_noise.SetFractalOctaves(octaves);
			_noise.SetFractalPersistence((float)persistence);
			_noise.SetFrequency((float)(1.0 / scale));

			return _noise.GetNoise((float)x, (float)y);
		}

		public IEnumerable<(Vector2, float)> GetHeights()
		{
			List<(Vector2,float)> list = new List<(Vector2, float)>();
			for (int i = 0; i < _heightMap.Size(); i++)
			{
				double v = _heightMap.Get(i);
				VoronoiPoint p = _heightMap.GetVertex(i);
				list.Add((new Vector2((float)p.X, (float)p.Y), (float)v));
			}
			return list;
		}
	}
}
