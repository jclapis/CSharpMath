namespace CSharpMath.Rendering {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using Display;
  using Enumerations;
  using Displays = Display.MathListDisplay<Fonts, Glyph>;

  public static class TextLayoutter {
    public static (Displays relative, Displays absolute) Layout(TextAtom input, Fonts inputFont, float canvasWidth, float additionalLineSpacing) {
#warning Multiply these constants by resolution
      const float abovedisplayskip = 12, abovedisplayshortskip = 0, belowdisplayskip = 12, belowdisplayshortskip = 7;
      if (input == null) return
          (new Displays(Array.Empty<IDisplay<Fonts, Glyph>>()),
           new Displays(Array.Empty<IDisplay<Fonts, Glyph>>()));
      float accumulatedHeight = 0;
      bool afterDisplayMaths = false; //indicator of the need to apply belowdisplay(short)skip when line break
      TextDisplayLineBuilder line = new TextDisplayLineBuilder();
      void BreakLine(List<IDisplay<Fonts, Glyph>> displayList, List<IDisplay<Fonts, Glyph>> displayMathList, bool appendLineGap = true) {
        if (afterDisplayMaths) {
          accumulatedHeight += line.Width > displayMathList.Last().Position.X ? belowdisplayskip : belowdisplayshortskip;
          afterDisplayMaths = false;
        }
        line.Clear(0, -accumulatedHeight, displayList, ref accumulatedHeight, appendLineGap, additionalLineSpacing);
      }
      void AddDisplaysWithLineBreaks(
        TextAtom atom,
        Fonts fonts,
        List<IDisplay<Fonts, Glyph>> displayList,
        List<IDisplay<Fonts, Glyph>> displayMathList,
        FontStyle style,
        Structures.Color? color
      ) {

        IDisplay<Fonts, Glyph> display;
        switch (atom) {
          case TextAtom.List list:
            foreach (var a in list.Content) AddDisplaysWithLineBreaks(a, fonts, displayList, displayMathList, style, color);
            break;
          case TextAtom.Style st:
            AddDisplaysWithLineBreaks(st.Content, fonts, displayList, displayMathList, st.FontStyle, color);
            break;
          case TextAtom.Size sz:
            AddDisplaysWithLineBreaks(sz.Content, new Fonts(fonts, sz.PointSize), displayList, displayMathList, style, color);
            break;
          case TextAtom.Color c:
            AddDisplaysWithLineBreaks(c.Content, fonts, displayList, displayMathList, style, c.Colour);
            break;
          case TextAtom.Space sp:
            //Allow space at start of line since user explicitly specified its length
            //Also \par generates this kind of spaces
            line.AddSpace(sp.Content.ActualLength(MathTable.Instance, fonts));
            break;
          case TextAtom.Newline n:
            BreakLine(displayList, displayMathList);
            break;
          case TextAtom.Math m when m.DisplayStyle:
            var lastLineWidth = line.Width;
            BreakLine(displayList, displayMathList, false);
            display = Typesetter<Fonts, Glyph>.CreateLine(m.Content, fonts, TypesettingContext.Instance, LineStyle.Display);
            var displayX = IPainterExtensions.GetDisplayPosition(display.Width, display.Ascent, display.Descent, fonts.PointSize, false, canvasWidth, float.NaN, TextAlignment.Top, default, default, default).X;
            //\because When displayList.LastOrDefault() is null, the false condition is selected
            //\therefore Append abovedisplayshortskip which defaults to 0 when nothing is above the display-style maths
            accumulatedHeight += lastLineWidth > displayX ? abovedisplayskip : abovedisplayshortskip;
            accumulatedHeight += display.Ascent;
            display.Position = new System.Drawing.PointF(displayX, -accumulatedHeight);
            accumulatedHeight += display.Descent;
            afterDisplayMaths = true;
            if (color != null) display.SetTextColorRecursive(color);
            displayMathList.Add(display);
            break;

            void FinalizeInlineDisplay(float ascender, float rawDescender, float lineGap, bool forbidAtLineStart = false) {
              if (color != null) display.SetTextColorRecursive(color);
              if (line.Width + display.Width > canvasWidth && !forbidAtLineStart)
                BreakLine(displayList, displayMathList);
              //rawDescender is taken directly from font file and is negative, while IDisplay.Descender is positive
              line.Add(display, ascender, -rawDescender, lineGap);
            }
          case TextAtom.Text t:
            var content = UnicodeFontChanger.Instance.ChangeFont(t.Content, style);
            var glyphs = GlyphFinder.Instance.FindGlyphs(fonts, content);
            //Calling Select(g => g.Typeface).Distinct() speeds up query up to 10 times,
            //Calling Max(Func<,>) instead of Select(Func<,>).Max() speeds up query 2 times
            var typefaces = glyphs.Select(g => g.Typeface).Distinct();
            WarningException.WarnIfAny(typefaces,
              tf => !Typography.OpenFont.Extensions.TypefaceExtensions.RecommendToUseTypoMetricsForLineSpacing(tf),
              "This font file is too old. Only font files that support standard typographical metrics are supported.");
            display = new TextRunDisplay<Fonts, Glyph>(Display.Text.AttributedGlyphRuns.Create(content, glyphs, fonts, false), t.Range, TypesettingContext.Instance);
            FinalizeInlineDisplay(
              typefaces.Max(tf => tf.Ascender * tf.CalculateScaleToPixelFromPointSize(fonts.PointSize)),
              typefaces.Min(tf => tf.Descender * tf.CalculateScaleToPixelFromPointSize(fonts.PointSize)),
              typefaces.Max(tf => tf.LineGap * tf.CalculateScaleToPixelFromPointSize(fonts.PointSize))
            );
            break;
          case TextAtom.Math m:
            if (m.DisplayStyle) throw new InvalidCodePathException("Display style maths should have been handled above this switch.");
            display = Typesetter<Fonts, Glyph>.CreateLine(m.Content, fonts, TypesettingContext.Instance, LineStyle.Text);
            var scale = fonts.MathTypeface.CalculateScaleToPixelFromPointSize(fonts.PointSize);
            FinalizeInlineDisplay(fonts.MathTypeface.Ascender * scale, fonts.MathTypeface.Descender * scale, fonts.MathTypeface.LineGap * scale);
            break;
          case TextAtom.ControlSpace cs:
            var spaceGlyph = GlyphFinder.Instance.Lookup(fonts, ' ');
            display = new TextRunDisplay<Fonts, Glyph>(Display.Text.AttributedGlyphRuns.Create(" ", new[] { spaceGlyph }, fonts, false), cs.Range, TypesettingContext.Instance);
            scale = spaceGlyph.Typeface.CalculateScaleToPixelFromPointSize(fonts.PointSize);
            FinalizeInlineDisplay(spaceGlyph.Typeface.Ascender * scale, spaceGlyph.Typeface.Descender * scale, spaceGlyph.Typeface.LineGap * scale,
              forbidAtLineStart: true); //No spaces at start of line
            break;
#warning ___WIP___
#if false
          case TextAtom.Accent a:
            float _GetSkew(IAccent accent, float accenteeWidth, TGlyph accentGlyph) {
              if (accent.Nucleus.Length == 0) {
                // no accent
                return 0;
              }
              float accentAdjustment = _mathTable.GetTopAccentAdjustment(_styleFont, accentGlyph);
              float accenteeAdjustment = 0;
              if (!_IsSingleCharAccent(accent)) {
                accenteeAdjustment = accenteeWidth / 2;
              } else {
                var innerAtom = accent.InnerList.Atoms[0];
                var accenteeGlyph = _context.GlyphFinder.FindGlyphForCharacterAtIndex(_font, innerAtom.Nucleus.Length - 1, innerAtom.Nucleus);
                accenteeAdjustment = _context.MathTable.GetTopAccentAdjustment(_styleFont, accenteeGlyph);
              }
              return accenteeAdjustment - accentAdjustment;
            }
            var (relative, absolute) = Layout(a.Content, fonts, canvasWidth, additionalLineSpacing);
            var accentee = new MathListDisplay<Fonts, Glyph>(new[] { relative, absolute });
            var rawAccentGlyph = GlyphFinder.Instance.Lookup(fonts, a.AccentChar);
            var accenteeWidth = accentee.Width;
            scale = rawAccentGlyph.Typeface.CalculateScaleToPixelFromPointSize(fonts.PointSize);
            var accentGlyph = _FindVariantGlyph(rawAccentGlyph, accenteeWidth, out float glyphAscent, out float glyphDescent, out float glyphWidth);
            var delta = Math.Min(accentee.Ascent, fonts.MathConsts.AccentBaseHeight.Value * scale);
            float skew = _GetSkew(accent, accenteeWidth, accentGlyph);
            var height = accentee.Ascent - delta;
            var accentPosition = new System.Drawing.PointF(skew, height);
            display = new GlyphDisplay<Fonts, Glyph>(accentGlyph, a.Range, fonts) {
              Ascent = glyphAscent,
              Descent = glyphDescent,
              Width = glyphWidth,
              Position = accentPosition
            };
            display = new AccentDisplay<Fonts, Glyph>(new GlyphDisplay<Fonts, Glyph>(accentGlyph, a.Range, fonts), new MathListDisplay<Fonts, Glyph>(new[] { relative, absolute }));
            FinalizeInlineDisplay(accentGlyph.Typeface.Ascender * scale, accentGlyph.Typeface.Descender * scale, accentGlyph.Typeface.LineGap * scale);
            break;
#endif
          case null:
            throw new InvalidOperationException("TextAtoms should never be null. You must have sneaked one in.");
          case var a:
            throw new InvalidCodePathException($"There should not be an unknown type of TextAtom. However, one with type {a.GetType()} was encountered.");
        }
      }
      var relativePositionList = new List<IDisplay<Fonts, Glyph>>();
      var absolutePositionList = new List<IDisplay<Fonts, Glyph>>();
      AddDisplaysWithLineBreaks(
        input,
        inputFont,
        relativePositionList,
        absolutePositionList,
        FontStyle.Roman /*FontStyle.Default is FontStyle.Italic, FontStyle.Roman is no change to characters*/,
        null
      );
      BreakLine(relativePositionList, absolutePositionList); //remember to finalize the last line
      return (new Displays(relativePositionList),
              new Displays(absolutePositionList));

    }
  }
}