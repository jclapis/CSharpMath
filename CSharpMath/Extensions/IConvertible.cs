using System;
using System.Globalization;

namespace CSharpMath {
  public static partial class Extensions {
    public static string ToStringInvariant<T>(this T value) where T : IConvertible =>
      value.ToString(CultureInfo.InvariantCulture);
  }
}