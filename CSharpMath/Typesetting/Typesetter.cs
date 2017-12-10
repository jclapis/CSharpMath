﻿using System;
using System.Collections.Generic;
using System.Text;
using CSharpMath.Atoms;
using CSharpMath.Display.Text;
using CSharpMath.Enumerations;
using CSharpMath.Display;
using CSharpMath.Interfaces;
using System.Drawing;
using System.Linq;
using CSharpMath.TypesetterInternal;
using CSharpMath.FrontEnd;

namespace CSharpMath {
  public class Typesetter<TMathFont, TGlyph>
    where TMathFont: MathFont<TGlyph> {
    private TMathFont _font;
    private readonly TypesettingContext<TMathFont, TGlyph> _context;
    private FontMathTable<TMathFont, TGlyph> _mathTable => _context.MathTable;
    private TMathFont _styleFont;
    private LineStyle _style;
    private bool _cramped;
    private bool _spaced;
    private List<IDisplay> _displayAtoms = new List<IDisplay>();
    private PointF _currentPosition; // the Y axis is NOT inverted in the typesetter.
    private AttributedString<TMathFont, TGlyph> _currentLine;
    private Range _currentLineIndexRange = Range.NotFoundRange;
    private List<IMathAtom> _currentAtoms = new List<IMathAtom>();
    private const int _delimiterFactor = 901;
    private const int _delimiterShortfallPoints = 5;

    private LineStyle _scriptStyle {
      get {
        switch (_style) {
          case LineStyle.Display:
          case LineStyle.Text:
            return LineStyle.Script;
          case LineStyle.Script:
          case LineStyle.ScriptScript:
            return LineStyle.ScriptScript;
          default:
            throw new InvalidOperationException();
        }
      }
    }

    private LineStyle _fractionStyle {
      get {
        if (_style == LineStyle.ScriptScript) {
          return _style;
        }
        return _style + 1;
      }
    }

    private bool _subscriptCramped => true;

    private bool _superscriptCramped => _cramped;

    private float _superscriptShiftUp {
      get {
        if (_cramped) {
          return _mathTable.SuperscriptShiftUpCramped(_styleFont);
        }
        return _mathTable.SuperscriptShiftUp(_styleFont);
      }
    }

    internal Typesetter(TMathFont font, TypesettingContext<TMathFont, TGlyph> context, LineStyle style, bool cramped, bool spaced) {
      _font = font;
      _context = context;
      _style = style;
      _styleFont = _context.MathFontCloner.Invoke(font, context.MathTable.GetStyleSize(style, font));
      _cramped = cramped;
      _spaced = spaced;
    }

    public static MathListDisplay CreateLine(IMathList list, TMathFont font, TypesettingContext<TMathFont, TGlyph> context, LineStyle style) {
      var finalized = list.FinalizedList();
      return _CreateLine(finalized, font, context, style, false);
    }

    private static MathListDisplay _CreateLine(
      IMathList list, TMathFont font, TypesettingContext<TMathFont, TGlyph> context,
      LineStyle style, bool cramped, bool spaced = false) {
      var preprocessedAtoms = _PreprocessMathList(list);
      var typesetter = new Typesetter<TMathFont, TGlyph>(font, context, style, cramped, spaced);
      typesetter._CreateDisplayAtoms(preprocessedAtoms);
      var lastAtom = list.Atoms.Last();
      var line = new MathListDisplay(typesetter._displayAtoms.ToArray());
      return line;
    }

    private void _CreateDisplayAtoms(List<IMathAtom> preprocessedAtoms) {
      IMathAtom prevNode = null;
      MathAtomType prevType = MathAtomType.MinValue;
      foreach (var atom in preprocessedAtoms) {
        switch (atom.AtomType) {
          case MathAtomType.Number:
          case MathAtomType.Variable:
          case MathAtomType.UnaryOperator:
            throw new InvalidOperationException($"Type {atom.AtomType} should have been removed by preprocessing");
          case MathAtomType.Boundary:
            throw new InvalidOperationException("A bounadry atom should never be inside a mathlist");
          case MathAtomType.Space:
            AddDisplayLine(false);
            var space = atom as MathSpace;
            _currentPosition.X += space.Space * _mathTable.MuUnit(_font);
            continue;
          case MathAtomType.Style:
            // stash the existing layout
            AddDisplayLine(false);
            var style = atom as IMathStyle;
            _style = style.Style;
            // We need to preserve the prevNode for any inter-element space changes,
            // so we skip to the next node.
            continue;
          case MathAtomType.Color:
            AddDisplayLine(false);
            var color = atom as IMathColor;
            var display = CreateLine(color.InnerList, _font, _context, _style);
            //            display.LocalTextColor = ColorExtensions.From6DigitHexString(color.ColorString);
            break;
          case MathAtomType.Fraction:
            AddDisplayLine(false);
            var fraction = atom as IFraction;
            AddInterElementSpace(prevNode, atom.AtomType);
            var fractionDisplay = MakeFraction(fraction);
            _displayAtoms.Add(fractionDisplay);
            _currentPosition.X += fractionDisplay.Width;
            if (atom.Superscript != null || atom.Subscript != null) {
              MakeScripts(atom, fractionDisplay, fraction.IndexRange.Location, 0);
            }
            break;
          case MathAtomType.Radical:
            AddDisplayLine(false);
            var rad = atom as IRadical;
            // Radicals are considered as Ord in rule 16.
            AddInterElementSpace(prevNode, MathAtomType.Ordinary);
            var displayRad = MakeRadical(rad.Radicand, rad.IndexRange);
            if (rad.Degree != null) {
              // add the degree to the radical
              var degree = CreateLine(rad.Degree, _font, _context, LineStyle.Script);
              displayRad.SetDegree(degree, _styleFont, _mathTable);
            }
            _displayAtoms.Add(displayRad);
            _currentPosition.X += displayRad.Width;

            if (atom.Superscript != null || atom.Subscript != null) {
              MakeScripts(atom, displayRad, rad.IndexRange.Location, 0);
            }
            break;
          case MathAtomType.Inner:
            AddDisplayLine(false);
            AddInterElementSpace(prevNode, atom.AtomType);
            var inner = atom as IMathInner;
            MathListDisplay innerDisplay;
            if (inner.LeftBoundary != null || inner.RightBoundary != null) {
              innerDisplay = _MakeLeftRight(inner);
            } else {
              innerDisplay = _CreateLine(inner.InnerList, _font, _context, _style, _cramped);
            }
            innerDisplay.Position = _currentPosition;
            _currentPosition.X += innerDisplay.Width;
            _displayAtoms.Add(innerDisplay);
            if (atom.Subscript != null || atom.Superscript != null) {
              MakeScripts(atom, innerDisplay, atom.IndexRange.Location, 0);
            }
            break;
          case MathAtomType.Ordinary:
          case MathAtomType.BinaryOperator:
          case MathAtomType.Relation:
          case MathAtomType.Open:
          case MathAtomType.Close:
          case MathAtomType.Placeholder:
          case MathAtomType.Punctuation:
            if (prevNode != null) {
              float interElementSpace = GetInterElementSpace(prevNode.AtomType, atom.AtomType);
              if (_currentLine.Length > 0) {
                if (interElementSpace > 0) {
                  // add a kerning of that space to the previous character.
                  // iosMath uses [NSString rangeOfComposedCharacterSequenceAtIndex: xxx] here.
                }
              } else {
                _currentPosition.X += interElementSpace;
              }
            }
            AttributedGlyphRun<TMathFont, TGlyph> current = null;
            var glyphs = _context.GlyphFinder.FindGlyphs(atom.Nucleus);
            if (atom.AtomType == MathAtomType.Placeholder) {
              current = AttributedGlyphRuns.Create<TMathFont, TGlyph>(glyphs, _placeholderColor);
            } else {
              current = AttributedGlyphRuns.Create<TMathFont, TGlyph>(glyphs, Color.Transparent);
            }
            _currentLine = AttributedStringExtensions.Combine(_currentLine, current);
            if (_currentLineIndexRange.Location == Range.UndefinedInt) {
              _currentLineIndexRange = atom.IndexRange;
            } else {
              _currentLineIndexRange.Length += atom.IndexRange.Length;
            }
            // add the fused atoms
            if (atom.FusedAtoms != null) {
              _currentAtoms.AddRange(atom.FusedAtoms);
            } else {
              _currentAtoms.Add(atom);
            }
            if (atom.Subscript != null || atom.Superscript != null) {
              var line = AddDisplayLine(true);
              float delta = 0;
              if (atom.Nucleus.IsNonEmpty()) {
                TGlyph glyph = _context.GlyphFinder.FindGlyphForCharacterAtIndex(atom.Nucleus.Length - 1, atom.Nucleus);
                delta = _context.MathTable.GetItalicCorrection(glyph);
              }
              if (delta > 0 && atom.Subscript == null) {
                // add a kern of delta
                _currentPosition.X += delta;
              }
              MakeScripts(atom, line, atom.IndexRange.End - 1, delta);
            }
            break;
        }

      }

      AddDisplayLine(false);
      if (_spaced && prevType != MathAtomType.MinValue) {
        var lastDisplay = _displayAtoms.LastOrDefault();
        if (lastDisplay != null) {
          float space = GetInterElementSpace(prevType, MathAtomType.Close);
          throw new NotImplementedException();
          //       lastDisplay.Width += space;
        }
      }
    }

    private void MakeScripts(IMathAtom atom, IDisplay display, int index, float delta) {
      float superscriptShiftUp = 0;
      float subscriptShiftDown = 0;
      display.HasScript = true;
      if (!(display is TextLineDisplay<TMathFont, TGlyph>)) {
        float scriptFontSize = GetStyleSize(_scriptStyle, _font);
        TMathFont scriptFont = _context.MathFontCloner.Invoke(_font, scriptFontSize);
        superscriptShiftUp = display.Ascent - _context.MathTable.SuperscriptBaselineDropMax(scriptFont);
        subscriptShiftDown = display.Descent + _context.MathTable.SubscriptBaselineDropMin(scriptFont);
      }
      if (atom.Superscript == null) {
        var line = display as TextLineDisplay<TMathFont, TGlyph>;
        Assertions.NotNull(atom.Subscript);
        var subscript = _CreateLine(atom.Subscript, _font, _context, _scriptStyle, _subscriptCramped);
        subscript.MyLinePosition = LinePosition.Subscript;
        subscript.IndexInParent = index;
        subscriptShiftDown = Math.Max(subscriptShiftDown, _mathTable.SubscriptShiftDown(_styleFont));
        subscriptShiftDown = Math.Max(subscriptShiftDown, subscript.Ascent - _mathTable.SubscriptTopMax(_styleFont));
        subscript.Position = new PointF(_currentPosition.X, _currentPosition.Y - subscriptShiftDown);
        _displayAtoms.Add(subscript);
        _currentPosition.X += subscript.Width + _mathTable.SpaceAfterScript(_styleFont);
        return;
      }

      // If we get here, superscript is not null
      var superscript = _CreateLine(atom.Superscript, _font, _context, _scriptStyle, _superscriptCramped);
      superscript.MyLinePosition = LinePosition.Supersript;
      superscript.IndexInParent = index;
      superscriptShiftUp = Math.Max(superscriptShiftUp, _superscriptShiftUp);
      superscriptShiftUp = Math.Max(superscriptShiftUp, superscript.Descent + _mathTable.SuperscriptBottomMin(_styleFont));
      if (atom.Subscript == null) {
        superscript.Position = new PointF(_currentPosition.X, _currentPosition.Y + superscriptShiftUp);
        _displayAtoms.Add(superscript);
        _currentPosition.X += superscript.Width + _mathTable.SpaceAfterScript(_styleFont);
        return;
      }
      // If we get here, we have both a superscript and a subscript.
      var subscriptB = _CreateLine(atom.Subscript, _font, _context, _scriptStyle, _subscriptCramped);
      subscriptB.MyLinePosition = LinePosition.Subscript;
      subscriptB.IndexInParent = index;
      subscriptShiftDown = Math.Max(subscriptShiftDown, _mathTable.SubscriptShiftDown(_styleFont));

      // joint positioning of subscript and superscript

      var subSuperScriptGap = (superscriptShiftUp - superscript.Descent + (subscriptShiftDown - subscriptB.Ascent));
      var gapShortfall = _mathTable.SubSuperscriptGapMin(_styleFont) - subSuperScriptGap;
      if (gapShortfall > 0) {
        subscriptShiftDown += gapShortfall;
        var superscriptBottomDelta = _mathTable.SuperscriptBottomMaxWithSubscript(_styleFont) - (superscriptShiftUp - superscript.Descent);
        if (superscriptBottomDelta > 0) {
          superscriptShiftUp += superscriptBottomDelta;
          subscriptShiftDown -= superscriptBottomDelta;
        }
      }
      // the delta is the italic correction above that shift superscript position.
      superscript.Position = new PointF(_currentPosition.X + delta, _currentPosition.Y + superscriptShiftUp);
      _displayAtoms.Add(superscript);
      subscriptB.Position = new PointF(_currentPosition.X, _currentPosition.Y - subscriptShiftDown);
      _displayAtoms.Add(subscriptB);
      _currentPosition.X += Math.Max(superscript.Width + delta, subscriptB.Width) + _mathTable.SpaceAfterScript(_styleFont);
    }

    private static Color _placeholderColor => Color.Blue;

    private float GetInterElementSpace(MathAtomType left, MathAtomType right) {
      var leftIndex = GetInterElementSpaceArrayIndexForType(left, true);
      var rightIndex = GetInterElementSpaceArrayIndexForType(right, false);
      var spaces = InterElementSpaces.Spaces;
      var spaceArray = spaces[leftIndex];
      var spaceType = spaceArray[rightIndex];
      Assertions.Assert(spaceType != InterElementSpaceType.Invalid, $"Invalid space between {left} and {right}");
      var multiplier = spaceType.SpacingInMu(_style);
      if (multiplier > 0) {
        return multiplier * _mathTable.MuUnit(_styleFont);
      }
      return 0;
    }

    private void AddInterElementSpace(IMathAtom prevNode, MathAtomType currentType) {
      float space = 0;
      if (prevNode != null) {
        space = GetInterElementSpace(prevNode.AtomType, currentType);
      } else if (_spaced) {
        space = GetInterElementSpace(MathAtomType.Open, currentType);
      }
      _currentPosition.X += space;
    }

    private int GetInterElementSpaceArrayIndexForType(MathAtomType atomType, bool row) {
      switch (atomType) {
        case MathAtomType.Color:
        case MathAtomType.Placeholder:
        case MathAtomType.Ordinary:
          return 0;
        case MathAtomType.LargeOperator:
          return 1;
        case MathAtomType.BinaryOperator:
          return 2;
        case MathAtomType.Relation:
          return 3;
        case MathAtomType.Open:
          return 4;
        case MathAtomType.Close:
          return 5;
        case MathAtomType.Punctuation:
          return 6;
        case MathAtomType.Fraction:
        case MathAtomType.Inner:
          return 7;
        case MathAtomType.Radical:
          if (row) {
            return 8;
          }
          throw new InvalidOperationException("Inter-element space undefined for radical on the right. Treat radical as ordinary.");
      }
      throw new InvalidOperationException($"Inter-element space undefined for atom type {atomType}");
    }

    private TextLineDisplay<TMathFont, TGlyph> AddDisplayLine(bool evenIfLengthIsZero) {
      if (evenIfLengthIsZero || (_currentLine != null && _currentLine.Length > 0)) {
        _currentLine.SetFont(_styleFont);
        var displayAtom = TextLineDisplays.Create(_currentLine, _currentLineIndexRange, _context, _currentAtoms);
        _displayAtoms.Add(displayAtom);
        _currentPosition.X += displayAtom.Width;
        _currentLine = new AttributedString<TMathFont, TGlyph>();
        _currentAtoms = new List<IMathAtom>();
        _currentLineIndexRange = Ranges.NotFound;
        return displayAtom;
      }
      return null;
    }

    private static List<IMathAtom> _PreprocessMathList(IMathList list) {
      IMathAtom prevNode = null;
      var r = new List<IMathAtom>();
      foreach (IMathAtom atom in list.Atoms) {
        // we do not use a switch statement on AtomType here as we may be changing said type.
        if (atom.AtomType == MathAtomType.Variable || atom.AtomType == MathAtomType.Number) {
          // These are not a TeX type nodes. TeX does this during parsing the input.
          // switch to using the font specified in the atom
          var newFont = _ChangeFont(atom.Nucleus, atom.FontStyle);
          // we convert it to ordinary
          atom.AtomType = MathAtomType.Ordinary;
          atom.Nucleus = newFont;
        }
        if (atom.AtomType == MathAtomType.Ordinary || atom.AtomType == MathAtomType.UnaryOperator) {
          // TeX treats unary operators as Ordinary. So will we.
          atom.AtomType = MathAtomType.Ordinary;
          // This is Rule 14 to merge ordinary characters.
          // combine ordinary atoms together
          if (prevNode != null && prevNode.AtomType == MathAtomType.Ordinary
            && prevNode.Superscript == null && prevNode.Subscript == null) {
            prevNode.Fuse(atom);
            // skip the current node as we fused it
            continue;
          }
        }
        // TODO: add italic correction here or in second pass?
        prevNode = atom;
        r.Add(prevNode);
      }
      return r;
    }
    private static string _ChangeFont(string input, FontStyle style) {
      var builder = new StringBuilder();
      var inputChars = input.ToCharArray();
      foreach (var inputChar in inputChars) {
        var unicode = _StyleCharacter(inputChar, style);
        builder.Append(unicode);
      }
      return builder.ToString();
    }

    private float GetStyleSize(LineStyle style, TMathFont font) {
      float original = font.PointSize;
      switch (style) {
        case LineStyle.Script:
          return original * _mathTable.ScriptScaleDown;
        case LineStyle.ScriptScript:
          return original * _mathTable.ScriptScriptScaleDown;
        default:
          return original;
      }
    }

    private static char _StyleCharacter(char inputChar, FontStyle style) {
      // TODO: deal with fonts here. The Objective c app uses 32-bit characters
      // for this, which probably means this method needs to return 32-bit characters,
      // with resulting changes cascading from there.
      return inputChar;
    }

    private float _radicalVerticalGap => (_style == LineStyle.Display)
      ? _mathTable.RadicalDisplayStyleVerticalGap
      : _mathTable.RadicalVerticalGap;

    private RadicalDisplay<TMathFont, TGlyph> MakeRadical(IMathList radicand, Range range) {
      var innerDisplay = _CreateLine(radicand, _font, _context, _style, true);
      var clearance = _radicalVerticalGap;
      var radicalRuleThickness = _mathTable.RadicalRuleThickness(_styleFont);
      var radicalHeight = innerDisplay.Ascent + innerDisplay.Descent + clearance + radicalRuleThickness;

      IDownshiftableDisplay glyph = _GetRadicalGlyph(radicalHeight);
      // Note this is a departure from Latex. Latex assumes that glyphAscent == thickness.
      // Open type math makes no such assumption, and ascent and descent are independent of the thickness.
      // Latex computes delta as descent - (h(inner) + d(inner) + clearance)
      // but since we may not have ascent == thickness, we modify the delta calculation slightly.
      // If the font designer followes Latex conventions, it will be identical.
      var delta = (glyph.Descent - glyph.Ascent) - (innerDisplay.Ascent + innerDisplay.Descent + clearance + radicalRuleThickness);
      if (delta > 0) {
        clearance += delta / 2;
      }
      // we need to shift the radical glyph up, to coincide with the baseline of inner.
      // The new ascent of the radical glyph should be thickness + adjusted clearance + h(inner)
      var radicalAscent = radicalRuleThickness + clearance + innerDisplay.Ascent;
      var shiftUp = radicalAscent - glyph.Ascent;   // Note: if the font designer followed latex conventions, this is the same as glyphAscent == thickness.
      glyph.ShiftDown = -shiftUp;

      var radical = new RadicalDisplay<TMathFont, TGlyph>(innerDisplay, glyph, _currentPosition, range);
      radical.Ascent = radicalAscent + _mathTable.RadicalExtraAscender(_styleFont);
      radical.TopKern = _mathTable.RadicalExtraAscender(_styleFont);
      radical.LineThickness = radicalRuleThickness;

      radical.Descent = Math.Max(glyph.Ascent + glyph.Descent - radicalAscent, innerDisplay.Descent);
      radical.Width = glyph.Width + innerDisplay.Width;
      return radical;
    }

    private float _NumeratorShiftUp(bool hasRule) {
      if (hasRule) {
        if (_style == LineStyle.Display) {
          return _mathTable.FractionNumeratorDisplayStyleShiftUp(_styleFont);
        }
        return _mathTable.FractionNumeratorShiftUp(_styleFont);
      }
      if (_style == LineStyle.Display) {
        return _mathTable.StackTopDisplayStyleShiftUp(_styleFont);
      }
      return _mathTable.StackTopShiftUp(_styleFont);
    }

    private float _NumeratorGapMin {
      get {
        if (_style == LineStyle.Display) {
          return _mathTable.FractionNumeratorDisplayStyleGapMin(_styleFont);
        }
        return _mathTable.FractionNumeratorGapMin(_styleFont);
      }
    }

    private float _DenominatorShiftDown(bool hasRule) {
      if (hasRule) {
        if (_style == LineStyle.Display) {
          return _mathTable.FractionDenominatorDisplayStyleShiftDown(_styleFont);
        }
        return _mathTable.FractionDenominatorShiftDown(_styleFont);
      }
      if (_style == LineStyle.Display) {
        return _mathTable.StackBottomDisplayStyleShiftDown(_styleFont);
      }
      return _mathTable.StackBottomShiftDown(_styleFont);
    }

    private float _DenominatorGapMin {
      get {
        if (_style == LineStyle.Display) {
          return _mathTable.FractionDenominatorDisplayStyleGapMin(_styleFont);
        }
        return _mathTable.FractionDenominatorGapMin(_styleFont);
      }
    }

    private float _StackGapMin {
      get {
        if (_style == LineStyle.Display) {
          return _mathTable.StackDisplayStyleGapMin(_styleFont);
        }
        return _mathTable.StackGapMin(_styleFont);
      }
    }

    private float _FractionDelimiterHeight {
      get {
        if (_style == LineStyle.Display) {
          return _mathTable.FractionDelimiterDisplayStyleSize(_styleFont);
        }
        return _mathTable.FractionDelimiterSize(_styleFont);
      }
    }

    private IDisplay MakeFraction(IFraction fraction) {
      var numeratorDisplay = _CreateLine(fraction.Numerator, _font, _context, _fractionStyle, false);
      var denominatorDisplay = _CreateLine(fraction.Denominator, _font, _context, _fractionStyle, true);

      var numeratorShiftUp = _NumeratorShiftUp(fraction.HasRule);
      var denominatorShiftDown = _DenominatorShiftDown(fraction.HasRule);
      var barLocation = _mathTable.AxisHeight(_styleFont);
      var barThickness = (fraction.HasRule) ? _mathTable.FractionRuleThickness(_styleFont) : 0;

      if (fraction.HasRule) {
        // this is the difference between the lowest portion of the numerator and the top edge of the fraction bar.
        var distanceFromNumeratorToBar = (numeratorShiftUp - numeratorDisplay.Descent) - (barLocation + barThickness / 2);
        // The distance should be at least displayGap
        if (distanceFromNumeratorToBar < _NumeratorGapMin) {
          numeratorShiftUp += (_NumeratorGapMin - distanceFromNumeratorToBar);
        }
        // now, do the same for the denominator
        var distanceFromDenominatorToBar = (barLocation - barThickness / 2) - (denominatorDisplay.Ascent - denominatorShiftDown);
        if (distanceFromNumeratorToBar < _DenominatorGapMin) {
          denominatorShiftDown += (_DenominatorGapMin - distanceFromDenominatorToBar);
        }
      } else {
        float clearance = (numeratorShiftUp - numeratorDisplay.Descent) - (denominatorDisplay.Ascent - denominatorShiftDown);
        float minClearance = _StackGapMin;
        if (clearance < minClearance) {
          numeratorShiftUp += (minClearance - clearance / 2);
          denominatorShiftDown += (minClearance - clearance) / 2;
        }
      }

      var display = new FractionDisplay(numeratorDisplay, denominatorDisplay, _currentPosition, fraction.IndexRange) {
        NumeratorUp = numeratorShiftUp,
        DenominatorDown = denominatorShiftDown,
        LineThickness = barThickness,
        LinePosition = barLocation
      };
      display.UpdateNumeratorAndDenominatorPositions();

      if (fraction.LeftDelimiter == null && fraction.RightDelimiter == null) {
        return display;
      }
      return _AddDelimitersToFractionDisplay(display, fraction);
    }

    private MathListDisplay _AddDelimitersToFractionDisplay(FractionDisplay display, IFraction fraction) {
      var glyphHeight = _FractionDelimiterHeight;
      var position = new PointF();
      var innerGlyphs = new List<IDisplay>();
      if (fraction.LeftDelimiter.IsNonEmpty()) {
        var leftGlyph = _FindGlyphForBoundary(fraction.LeftDelimiter, glyphHeight);
        leftGlyph.SetPosition(position);
        innerGlyphs.Add(leftGlyph);
        position.X += leftGlyph.Width;
      }
      display.Position = position;
      position.X += display.Width;
      innerGlyphs.Add(display);
      if (fraction.RightDelimiter.IsNonEmpty()) {
        var rightGlyph = _FindGlyphForBoundary(fraction.RightDelimiter, glyphHeight);
        rightGlyph.SetPosition(position);
        innerGlyphs.Add(rightGlyph);
        position.X += rightGlyph.Width;
      }
      var innerDisplay = new MathListDisplay(innerGlyphs.ToArray());
      innerDisplay.Position = _currentPosition;
      return innerDisplay;
    }

    private MathListDisplay _MakeLeftRight(IMathInner inner) {
      if (inner.LeftBoundary == null && inner.RightBoundary == null) {
        throw new InvalidOperationException("Inner should have a boundary to call this function.");
      }
      var innerListDisplay = _CreateLine(inner.InnerList, _font, _context, _style, _cramped, true);
      float axisHeight = _mathTable.AxisHeight(_styleFont);
      // delta is the max distance from the axis.
      float delta = Math.Max(innerListDisplay.Ascent - axisHeight, innerListDisplay.Descent + axisHeight);
      var d1 = (delta / 500) * _delimiterFactor;
      float d2 = 2 * delta - _delimiterShortfallPoints;
      float glyphHeight = Math.Max(d1, d2);

      var innerElements = new List<IDisplay>();
      var innerPosition = new PointF();
      if (inner.LeftBoundary != null && inner.LeftBoundary.Nucleus.IsNonEmpty()) {
        var leftGlyph = _FindGlyphForBoundary(inner.LeftBoundary.Nucleus, glyphHeight);
        leftGlyph.SetPosition(innerPosition);
        innerPosition.X += leftGlyph.Width;
        innerElements.Add(leftGlyph);
      }
      innerListDisplay.Position = innerPosition;
      innerPosition.X += innerListDisplay.Width;
      innerElements.Add(innerListDisplay);

      if (inner.RightBoundary != null && inner.RightBoundary.Nucleus.Length > 0) {
        var rightGlyph = _FindGlyphForBoundary(inner.RightBoundary.Nucleus, glyphHeight);
        rightGlyph.SetPosition(innerPosition);
        innerPosition.X += rightGlyph.Width;
        innerElements.Add(rightGlyph);
      }
      var innerArrayDisplay = new MathListDisplay(innerElements.ToArray());
      return innerArrayDisplay;
    }

    private Range _RangeOfComposedCharacterSequenceAtIndex(int index) {
      // This will likely change once we start dealing with fonts and weird characters.
      return new Range(index, 1);
    }


    private IDownshiftableDisplay _FindGlyphForBoundary(string delimiter, float glyphHeight) {
      float glyphAscent, glyphDescent, glyphWidth;
      TGlyph leftGlyph = _context.GlyphFinder.FindGlyphForCharacterAtIndex(0, delimiter);
      TGlyph glyph = _FindGlyph(leftGlyph, glyphHeight, out glyphAscent, out glyphDescent, out glyphWidth);
      IDownshiftableDisplay glyphDisplay = null;
      if (glyphAscent + glyphDescent < glyphHeight) {
        // Not yet implemented -- construct a glyph.
      }
      if (glyphDisplay == null) {
        glyphDisplay = new GlyphDisplay<TGlyph>(glyph, Range.NotFoundRange, _styleFont) {
          Ascent = glyphAscent,
          Descent = glyphDescent,
          Width = glyphWidth
        };
        // Center the glyph on the axis
        var shiftDown = 0.5f * (glyphDisplay.Ascent - glyphDisplay.Descent) - _mathTable.AxisHeight(_styleFont);
        glyphDisplay.ShiftDown = shiftDown;
      }
      return glyphDisplay;
    }

    private IDownshiftableDisplay _GetRadicalGlyph(float radicalHeight) {
      TGlyph radicalGlyph = _context.GlyphFinder.FindGlyphForCharacterAtIndex(0, "\u221A");
      TGlyph glyph = _FindGlyph(radicalGlyph, radicalHeight, out float glyphAscent, out float glyphDescent, out float glyphWidth);

      IDownshiftableDisplay glyphDisplay = null;
      if (glyphAscent + glyphDescent < radicalHeight) {
        // the glyphs are not beg enough, so we construct one using extenders
        glyphDisplay = _ConstructGlyph(radicalGlyph, radicalHeight);
      }
      if (glyphDisplay == null) {
        glyphDisplay = new GlyphDisplay<TGlyph>(glyph, Range.NotFoundRange, _styleFont) {
          Ascent = glyphAscent,
          Descent = glyphDescent,
          Width = glyphWidth
        };
      }
      return glyphDisplay;
    }

    private GlyphConstructionDisplay<TGlyph> _ConstructGlyph(TGlyph glyph, float glyphHeight) {
      GlyphPart<TGlyph>[] parts = _mathTable.GetVerticalGlyphAssembly(glyph);
      if (parts.IsEmpty()) {
        return null;
      }
      List<TGlyph> glyphs = new List<TGlyph>();
      List<float> offsets = new List<float>();
      float height = _ConstructGlyphWithParts(parts, glyphHeight, glyphs, offsets);
      TGlyph firstGlyph = glyphs[0];
      float width = _context.GlyphBoundsProvider.GetAdvancesForGlyphs(_styleFont, new TGlyph[] { firstGlyph });
      var display = new GlyphConstructionDisplay<TGlyph>(glyphs, offsets, _styleFont) {
        Width = width,
        Ascent = height,
        Descent = 0 // it's up to the rendering to adjust the display glyph up or down
      };
      return display;
    }

    private float _ConstructGlyphWithParts(GlyphPart<TGlyph>[] parts, float glyphHeight, List<TGlyph> glyphs, List<float> offsets) {
      for (int nExtenders = 0; true; nExtenders++) {
        GlyphPart<TGlyph> prevPart = null;
        float minDistance = _mathTable.MinConnecterGap(_styleFont);
        float minOffset = 0;
        float maxDelta = float.MaxValue;
        foreach (var part in parts) {
          var repeats = 1;
          if (part.IsExtender) {
            repeats = nExtenders;
          }
          for (int i=0; i<repeats; i++) {
            glyphs.Add(part.Glyph);
            if (prevPart!=null) {
              float maxOverlap = Math.Min(prevPart.EndConnectorLength, part.StartConnectorLength);
              // the minimum amount we can add to the offset
              float minOffsetDelta = prevPart.FullAdvance - maxOverlap;
              // the maximum amount we can add to the offset
              float maxOffsetDelta = prevPart.FullAdvance - minOffsetDelta;
              minOffset = minOffset + minOffsetDelta;
            }
            offsets.Add(minOffset);
            prevPart = part;
          }
        }
        if (prevPart == null) {
          continue; // maybe only extenders
        }
        float minHeight = minOffset + prevPart.FullAdvance;
        float maxHeight = minHeight + maxDelta * (glyphs.Count - 1);
        if (minHeight >= glyphHeight) {
          // we are done
          return minHeight;
        }
        if (glyphHeight <= maxHeight) {
          // spread the delta equally among all the connecters
          float delta = glyphHeight - minHeight;
          float dDelta = delta / (glyphs.Count - 1);
          float lastOffset = 0;
          for (int i=0; i<offsets.Count; i++) {
            float offset = offsets[i] + i * dDelta;
            offsets[i] = offset;
            lastOffset = offset;
          }
          // we are done
          return lastOffset + prevPart.FullAdvance;
        }
      }
    }

    private TGlyph _FindGlyph(TGlyph rawGlyph, float height,
      out float glyphAscent, out float glyphDescent, out float glyphWidth) {
      // Todo -- flesh out. See findGlyph:withHeight:glyphAscent:glyphDescent:glyphWidth: 
      // in iosMath.
      var glyph = rawGlyph;
      var rect = _context.GlyphBoundsProvider.GetBoundingRectForGlyphs(_font, new TGlyph[] { glyph });
      rect.GetAscentDescentWidth(out glyphAscent, out glyphDescent, out glyphWidth);
      return glyph;
    }

    private TGlyph _FindGlyphDisplay(TGlyph glyph, float height, out float glyphAscent, out float glyphDescent, out float glyphWidth) {
      // TODO: flesh this out
      var bounds = _context.GlyphBoundsProvider.GetBoundingRectForGlyphs(_font, new TGlyph[] { glyph });
      bounds.GetAscentDescentWidth(out glyphAscent, out glyphDescent, out glyphWidth);
      return glyph;

    }



  }
}