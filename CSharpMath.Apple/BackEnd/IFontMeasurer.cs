namespace CSharpMath.Display.FrontEnd {
  public interface IFontMeasurer<TFont, TGlyph> where TFont: IFont<TGlyph> {
    /// <summary>A proportionality constant that is applied when
    /// reading from the Json table.</summary>
    int GetUnitsPerEm(TFont font);
  }
}