using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace OLearyMapGen.math
{
	internal static class Drawing
	{
		public static HashSet<Point> DilateLine(IEnumerable<Point> linePoints, double thickness)
		{
			// Use HashSet to avoid duplicate points
			HashSet<Point> dilatedPoints = new HashSet<Point>();
			double radius = thickness / 2.0;

			foreach (Point p in linePoints)
			{
				// Add points in a square neighborhood around the current point
				for (int y = p.Y - (int)Math.Ceiling(radius); y <= p.Y + (int)Math.Ceiling(radius); y++)
				{
					for (int x = p.X - (int)Math.Ceiling(radius); x <= p.X + (int)Math.Ceiling(radius); x++)
					{
						// Use a circular check for a rounded line cap/joint
						if ((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y) <= radius * radius)
						{
							dilatedPoints.Add(new Point(x, y));
						}
					}
				}
			}

			return dilatedPoints;
		}

		public static IEnumerable<Point> GetPointsOnLine(Vector2 a, Vector2 b, int scalar)
		{
			// 189.09,16.69 -> -1,-1
			a *= scalar;
			b *= scalar;

			a -= Vector2.Normalize(b - a) * 1.5f;
			b += Vector2.Normalize(b - a) * 1.5f;

			return GetPointsOnLine((int)a.X, (int)a.Y, (int)b.X, (int)b.Y, scalar);
		}

		public static IEnumerable<Point> GetPointsOnLine(int x1, int y1, int x2, int y2, int square_width)
		{
			int dx = x2 - x1;
			int dy = y2 - y1;
			int dx_x = dx > 0 ? 1 : (dx < 0 ? -1 : 0);
			int dy_y = dy > 0 ? 1 : (dy < 0 ? -1 : 0);

			// --- Initialization (Unchanged) ---
			int local_x = x1 % square_width;
			int local_y = y1 % square_width;
			int x_dist = (dx >= 0) ? (square_width - local_x) : (local_x);
			int y_dist = (dy >= 0) ? (square_width - local_y) : (local_y);
			int cross_product = Math.Abs(dx) * Math.Abs(y_dist) - Math.Abs(dy) * Math.Abs(x_dist);
			int dx_cross = -Math.Abs(dy) * square_width;
			int dy_cross = Math.Abs(dx) * square_width;

			int x = x1 / square_width;
			int y = y1 / square_width;
			int end_x = x2 / square_width;
			int end_y = y2 / square_width;

			// --- Boundary Correction (Unchanged) ---
			if (dy < 0)
			{
				if ((y1 % square_width) == 0)
				{
					y--;
					cross_product += dy_cross;
				}
			}
			else if (dy > 0)
			{
				if ((y2 % square_width) == 0)
					end_y--;
			}

			if (dx < 0)
			{
				if ((x1 % square_width) == 0)
				{
					x--;
					cross_product += dx_cross;
				}
			}
			else if (dx > 0)
			{
				if ((x2 % square_width) == 0)
					end_x--;
			}
			// ------------------------------------
			int totalPoints = 0;
			int px, py;
			px = x-dx;
			py = y-dy;
			while (true)
			{
				yield return new Point(x, y);
				totalPoints++;
				if (totalPoints > int.MaxValue / 2 || (totalPoints > 500 && x <= 0 && y <= 0))
				{
					;
				}

				if (Vector2.Distance(new Vector2(x,y), new Vector2(end_x, end_y)) >= 2+Vector2.Distance(new Vector2(px, py), new Vector2(end_x, end_y)))
				{
					;
				}

				if (Vector2.Distance(new Vector2(x, y), new Vector2(px, py)) + Vector2.Distance(new Vector2(x, y), new Vector2(end_x, end_y)) > 4 * Vector2.Distance(new Vector2(px, py), new Vector2(end_x, end_y)))
				{
					;
				}

				// 1. Check for termination immediately after yielding the current point
				if (x == end_x && y == end_y)
				{
					break;
				}

				// 2. Determine movement based on error term
				int old_cross = cross_product;
				bool move_x = old_cross >= 0;
				bool move_y = old_cross <= 0;

				// 3. --- Boundary Saturation / Override Logic ---

				// If X has reached its destination, we MUST move Y (unless Y is also done).
				if (x == end_x && y != end_y)
				{
					move_x = false; // Cannot move X further
					move_y = true;  // Force Y movement to reach the endpoint
				}
				// If Y has reached its destination, we MUST move X (unless X is also done).
				else if (y == end_y && x != end_x)
				{
					move_x = true;  // Force X movement to reach the endpoint
					move_y = false; // Cannot move Y further
				}

				// 4. Execute movement and update cross product

				if (move_x)
				{
					x += dx_x;
					cross_product += dx_cross;
				}

				if (move_y)
				{
					y += dy_y;
					cross_product += dy_cross;
				}

				// Safety check: If neither move happened, but we haven't broken, 
				// something is fundamentally wrong (shouldn't happen with the override).
				// If (!move_x && !move_y) { break; } 
				// The termination check at the top handles this cleanly.
			}
		}

		private static void Swap<T>(ref T a, ref T b)
		{
			T c = a;
			a = b;
			b = c;
		}
	}
}
