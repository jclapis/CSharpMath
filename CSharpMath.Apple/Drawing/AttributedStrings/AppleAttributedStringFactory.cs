﻿using System;
using System.Collections.Generic;
using System.Text;
using CSharpMath.Display.Text;
using Foundation;
using CoreText;
using TGlyph = System.UInt16;
using TMathFont = CSharpMath.Apple.AppleMathFont;

namespace CSharpMath.Apple.Drawing {
  public class AppleAttributedStringFactory {
    private readonly UnicodeGlyphFinder _glyphFinder;

    public AppleAttributedStringFactory(UnicodeGlyphFinder glyphFinder) {
      _glyphFinder = glyphFinder;
    }
    //public CTRun FromAttributedGlyphRun(AttributedGlyphRun<TMathFont, TGlyph> glyphRun) {
    //  var attributes = AppleAttributeDictionaryFactory.FromAttributedGlyphRun(glyphRun);
      
    //}
  }
}
