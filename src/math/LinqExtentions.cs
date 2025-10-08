using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace OLearyMapGen.math
{
	public static class LinqExtentions
	{
		public static IEnumerable<int> Digitize<T>(IEnumerable<T> input, T[] source) where T : IComparable<T>
		{
			foreach (T item in input)
			{
				for (int index = 0; index < source.Length - 1; index++)
				{
					if (item.CompareTo(source[index]) < 0)
					{
						yield return index;
						break;
					}
				}

				yield return source.Length;
			}

		}
	}
}
