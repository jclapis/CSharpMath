namespace CSharpMath.Editor {
  using System;
  using System.Drawing;
  using System.Linq;

  using Display;
  using Display.Text;
  using FrontEnd;
  using Color = Structures.Color;

  partial class DisplayEditingExtensions {
    public static MathListIndex IndexForPoint<TFont, TGlyph>(this FractionDisplay<TFont, TGlyph> self, TypesettingContext<TFont, TGlyph> context, PointF point) where TFont : IFont<TGlyph> {
      // We can be before or after the fraction
      if (point.X < self.Position.X - PixelDelta)
        //We are before the fraction, so
        return MathListIndex.Level0Index(self.Range.Location);
      else if (point.X > self.Position.X + self.Width + PixelDelta)
        //We are after the fraction
        return MathListIndex.Level0Index(self.Range.End);

      //We can be either near the numerator or denominator
      var numeratorDistance = DistanceFromPointToRect(point, self.Numerator.DisplayBounds);
      var denominatorDistance = DistanceFromPointToRect(point, self.Denominator.DisplayBounds);
      if (numeratorDistance < denominatorDistance)
        return MathListIndex.IndexAtLocation(self.Range.Location, self.Numerator.IndexForPoint(context, point), MathListSubIndexType.Numerator);
      else
        return MathListIndex.IndexAtLocation(self.Range.Location, self.Denominator.IndexForPoint(context, point), MathListSubIndexType.Denominator);
    }
    ///<summary>Seems never used</summary>
    public static PointF? PointForIndex<TFont, TGlyph>(this FractionDisplay<TFont, TGlyph> self, TypesettingContext<TFont, TGlyph> context, MathListIndex index) where TFont : IFont<TGlyph> {
      if (index.SubIndexType != MathListSubIndexType.None)
        throw Arg("The subindex must be none to get the closest point for it.", nameof(index));
      // draw a caret before the fraction
      return self.Position;
    }
    public static void HighlightCharacterAt<TFont, TGlyph>(this TextLineDisplay<TFont, TGlyph> self, TypesettingContext<TFont, TGlyph> context, MathListIndex index, Color color) where TFont : IFont<TGlyph> {
    }
    }
}
