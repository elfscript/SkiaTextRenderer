using System;
using System.Collections.Generic;
using SkiaSharp;

namespace SkiaTextRenderer
{
    public static partial class TextRendererSk
    {
        private static readonly string[] NewLineCharacters = new[] { Environment.NewLine, UnicodeCharacters.NewLine.ToString(), UnicodeCharacters.CarriageReturn.ToString() };

        private static FontCache FontCache;
        private static readonly SKPaint TextPaint = new SKPaint();
        private static float LineHeight { get => TextPaint.FontMetrics.Descent - TextPaint.FontMetrics.Ascent; }
        private static FontStyle TextStyle;
        private static string Text;
        private static TextFormatFlags Flags;
        private static TextPaintOptions PaintOptions;
        private static float MaxLineWidth;
        private static SKRect Bounds = SKRect.Empty;

        private static SKSize ContentSize = SKSize.Empty;
        private static float LeftPadding
        {
            get
            {
                if (Flags.HasFlag(TextFormatFlags.NoPadding))
                    return 0;

                if (Flags.HasFlag(TextFormatFlags.LeftAndRightPadding))
                    return (float)Math.Ceiling((TextPaint.FontSpacing / 6.0) * 2.0);

                if (Flags.HasFlag(TextFormatFlags.GlyphOverhangPadding))
                    return (float)Math.Ceiling(TextPaint.FontSpacing / 6.0);

                return 0;
            }
        }
        private static float RightPadding
        {
            get
            {
                if (Flags.HasFlag(TextFormatFlags.NoPadding))
                    return 0;

                if (Flags.HasFlag(TextFormatFlags.LeftAndRightPadding))
                    return (float)Math.Ceiling((TextPaint.FontSpacing / 6.0) * 2.5);

                if (Flags.HasFlag(TextFormatFlags.GlyphOverhangPadding))
                    return (float)Math.Ceiling((TextPaint.FontSpacing / 6.0) * 1.5);

                return 0;
            }
        }

        private static bool EnableWrap { get => (Flags & (TextFormatFlags.NoClipping | TextFormatFlags.SingleLine)) == 0; }
        private static bool LineBreakWithoutSpaces { get => (Flags & TextFormatFlags.WordBreak) == 0; }
        //--> LineBreakWithoutSpaces == !TextWrapByWord

        private static int NumberOfLines;
        private static float TextDesiredHeight;
        private static List<float> LinesWidth = new List<float>();
        private static List<float> LinesOffsetX = new List<float>();
        private static float LetterOffsetY;

        class LetterInfo
        {
            public char Character;
            public bool Valid;
            public float PositionX;
            public float PositionY;
            public int LineIndex;
        }
        private static List<LetterInfo> LettersInfo = new List<LetterInfo>();

        private delegate int GetFirstCharOrWordLength(string textLine, int startIndex);

        private static void PrepareTextPaint(Font font)
        {
            FontCache = FontCache.GetCache(font.Typeface, font.Size);

            TextPaint.IsStroke = false;
            TextPaint.HintingLevel = SKPaintHinting.Normal;
            TextPaint.IsAutohinted = true; // Only for freetype
            TextPaint.IsEmbeddedBitmapText = true;
            //TextPaint.DeviceKerningEnabled = true;

            TextPaint.Typeface = font.Typeface;
            TextPaint.TextSize = font.Size;

            if (font.Style == FontStyle.Italic)
                TextPaint.TextSkewX = -0.4f;
            else
                TextPaint.TextSkewX = 0;

            TextStyle = font.Style;
        }
        private static int GetFirstCharLength(string textLine, int startIndex)
        {
            return 1;
        }

        private static int GetFirstWordLength(string textLine, int startIndex)
        {
            int length = 0;
            float nextLetterX = 0;

            for (int index = startIndex; index < textLine.Length; ++index)
            {
                var character = textLine[index];

                if (character == UnicodeCharacters.NewLine || character == UnicodeCharacters.CarriageReturn
                    || (!Utils.IsUnicodeNonBreaking(character) && (Utils.IsUnicodeSpace(character) || Utils.IsCJKUnicode(character))))
                {
                    break;
                }

                if (!FontCache.GetLetterDefinitionForChar(character, out var letterDef))
                {
                    break;
                }

                if (MaxLineWidth > 0)
                {
                    if (nextLetterX + letterDef.AdvanceX > MaxLineWidth)
                        break;
                }

                nextLetterX += letterDef.AdvanceX;

                length++;
            }

            if (length == 0 && textLine.Length > 0)
                length = 1;

            return length;
        }
        private static void RecordLetterInfo(SKPoint point, char character, int letterIndex, int lineIndex)
        {
            if (letterIndex >= LettersInfo.Count)
            {
                LettersInfo.Add(new LetterInfo());
            }

            LettersInfo[letterIndex].LineIndex = lineIndex;
            LettersInfo[letterIndex].Character = character;
            LettersInfo[letterIndex].Valid = FontCache.GetLetterDefinitionForChar(character, out var letterDef) && letterDef.ValidDefinition;
            LettersInfo[letterIndex].PositionX = point.X;
            LettersInfo[letterIndex].PositionY = point.Y;
        }
        private static void RecordPlaceholderInfo(int letterIndex, char character)
        {
            if (letterIndex >= LettersInfo.Count)
            {
                LettersInfo.Add(new LetterInfo());
            }

            LettersInfo[letterIndex].Character = character;
            LettersInfo[letterIndex].Valid = false;
        }
        private static void MultilineTextWrapByWord()
        {
            MultilineTextWrap(GetFirstWordLength);
        }
        private static void MultilineTextWrapByChar()
        {
            MultilineTextWrap(GetFirstCharLength);
        }
        private static void MultilineTextWrap(GetFirstCharOrWordLength nextTokenLength)
        {
            int textLength = Text.Length;
            int lineIndex = 0;
            float nextTokenX = 0;
            float nextTokenY = 0;
            float longestLine = 0;
            float letterRight = 0;
            float nextWhitespaceWidth = 0;
            FontLetterDefinition letterDef;
            SKPoint letterPosition = new SKPoint();
            bool nextChangeSize = true;

            for (int index = 0; index < textLength;)
            {
                char character = Text[index];
                if (character == UnicodeCharacters.NewLine)
                {
                    if (!Flags.HasFlag(TextFormatFlags.SingleLine))
                    {
                        LinesWidth.Add(letterRight);
                        letterRight = 0;
                        lineIndex++;
                        nextTokenX = 0;
                        nextTokenY += LineHeight;
                    }

                    RecordPlaceholderInfo(index, character);
                    index++;
                    continue;
                }

                var tokenLen = nextTokenLength(Text, index);
                float tokenRight = letterRight;
                float nextLetterX = nextTokenX;
                float whitespaceWidth = nextWhitespaceWidth;
                bool newLine = false;
                for (int tmp = 0; tmp < tokenLen; ++tmp)
                {
                    int letterIndex = index + tmp;
                    character = Text[letterIndex];

                    // ignore \r ?
                    if (character == UnicodeCharacters.CarriageReturn)
                    {
                        RecordPlaceholderInfo(letterIndex, character);
                        continue;
                    }

                    // \b - Next char not change x position
                    if (character == UnicodeCharacters.NextCharNoChangeX)
                    {
                        nextChangeSize = false;
                        RecordPlaceholderInfo(letterIndex, character);
                        continue;
                    }

                    if (!FontCache.GetLetterDefinitionForChar(character, out letterDef))
                    {
                        RecordPlaceholderInfo(letterIndex, character);
                        Console.WriteLine($"TextRenderer.MultilineTextWrap error: can't find letter definition in font file for letter: {character}");
                        continue;
                    }

                    if (EnableWrap && MaxLineWidth > 0 && nextTokenX > 0 && nextLetterX + letterDef.AdvanceX > MaxLineWidth
                        && !Utils.IsUnicodeSpace(character) && nextChangeSize)
                    {
                        LinesWidth.Add(letterRight - whitespaceWidth);
                        nextWhitespaceWidth = 0f;
                        letterRight = 0f;
                        lineIndex++;
                        nextTokenX = 0f;
                        nextTokenY += LineHeight;
                        newLine = true;
                        break;
                    }
                    else
                    {
                        letterPosition.X = nextLetterX;
                    }

                    letterPosition.Y = nextTokenY;
                    RecordLetterInfo(letterPosition, character, letterIndex, lineIndex);

                    if (nextChangeSize)
                    {
                        var newLetterWidth = letterDef.AdvanceX;

                        nextLetterX += newLetterWidth;
                        tokenRight = nextLetterX;

                        if (Utils.IsUnicodeSpace(character))
                        {
                            nextWhitespaceWidth += newLetterWidth;
                        }
                        else
                        {
                            nextWhitespaceWidth = 0;
                        }
                    }

                    nextChangeSize = true;
                }

                if (newLine)
                {
                    continue;
                }

                nextTokenX = nextLetterX;
                letterRight = tokenRight;

                index += tokenLen;
            }

            if (LinesWidth.Count == 0)
            {
                LinesWidth.Add(letterRight);
                longestLine = letterRight;
            }
            else
            {
                LinesWidth.Add(letterRight - nextWhitespaceWidth);
                foreach (var lineWidth in LinesWidth)
                {
                    if (longestLine < lineWidth)
                        longestLine = lineWidth;
                }
            }

            NumberOfLines = lineIndex + 1;
            TextDesiredHeight = NumberOfLines * LineHeight;

            ContentSize.Width = longestLine + LeftPadding + RightPadding;
            ContentSize.Height = TextDesiredHeight;
        }

        private static void ComputeAlignmentOffset()
        {
            LinesOffsetX.Clear();

            if (Flags.HasFlag(TextFormatFlags.HorizontalCenter))
            {
                foreach (var lineWidth in LinesWidth)
                    LinesOffsetX.Add((Bounds.Width - lineWidth) / 2f);
            }
            else if (Flags.HasFlag(TextFormatFlags.Right))
            {
                foreach (var lineWidth in LinesWidth)
                    LinesOffsetX.Add(Bounds.Width - lineWidth);
            }
            else
            {
                for (int i = 0; i < NumberOfLines; i++)
                    LinesOffsetX.Add(0);
            }

            if (Flags.HasFlag(TextFormatFlags.VerticalCenter))
            {
                LetterOffsetY = (Bounds.Height - TextDesiredHeight) / 2;
            }
            else if (Flags.HasFlag(TextFormatFlags.Bottom))
            {
                LetterOffsetY = Bounds.Height - TextDesiredHeight;
            }
            else
            {
                LetterOffsetY = 0;
            }
        }

        private static void ComputeLetterPositionInBounds(ref SKRect bounds)
        {
            for (int i = 0; i < Text.Length; i++)
            {
                var letterInfo = LettersInfo[i];

                if (!letterInfo.Valid)
                    continue;

                var posX = letterInfo.PositionX + LinesOffsetX[letterInfo.LineIndex] + bounds.Left;
                if (!Flags.HasFlag(TextFormatFlags.HorizontalCenter))
                    posX += LeftPadding;

                var posY = letterInfo.PositionY + LetterOffsetY + bounds.Top;
                if (Flags.HasFlag(TextFormatFlags.ExternalLeading))
                    posY += TextPaint.FontMetrics.Leading;

                letterInfo.PositionX = posX;
                letterInfo.PositionY = posY;
            }
        }

        private static void AlignText()
        {
            if (string.IsNullOrEmpty(Text))
            {
                ContentSize = SKSize.Empty;
                return;
            }

            FontCache.PrepareLetterDefinitions(Text);

            TextDesiredHeight = 0;
            LinesWidth.Clear();

            if (MaxLineWidth > 0 && !LineBreakWithoutSpaces)
                MultilineTextWrapByWord();
            else
                MultilineTextWrapByChar();
        }

        public static SKSize MeasureText(string text, Font font)
        {
            return MeasureText(text, font, 0, TextFormatFlags.Default);
        }
        public static SKSize MeasureText(string text, Font font, float maxLineWidth)
        {
            return MeasureText(text, font, maxLineWidth, TextFormatFlags.Default);
        }

        public static SKSize MeasureText(string text, Font font, float maxLineWidth, TextFormatFlags flags)
        {
            Text = text;
            Flags = flags;
            MaxLineWidth = maxLineWidth;

            PrepareTextPaint(font);

            AlignText();

            return ContentSize;
        }

        private static HashSet<int> LinesHadDrawedUnderlines = new HashSet<int>();

        private static void DrawCursorForEmptyString(SKCanvas canvas, Font font, ref SKColor foreColor)
        {
            TextPaint.TextSize = font.Size;
            TextPaint.Color = foreColor;
            canvas.DrawLine(new SKPoint(0, 0), new SKPoint(0, LineHeight), TextPaint);
        }

        private static void DrawCursorIfNeed(SKCanvas canvas)
        {
            if (PaintOptions == null || PaintOptions.CursorPosition == null)
                return;

            var pos1 = new SKPoint();
            var pos2 = new SKPoint();

            if (PaintOptions.CursorPosition == Text.Length)
            {
                var letterInfo = LettersInfo[PaintOptions.CursorPosition.Value - 1];
                FontLetterDefinition letterDef;
                FontCache.GetLetterDefinitionForChar(letterInfo.Character, out letterDef);

                pos1.X = letterInfo.PositionX + letterDef.AdvanceX;
                pos1.Y = letterInfo.PositionY;
                pos2.X = pos1.X;
                pos2.Y = letterInfo.PositionY + LineHeight;
            }
            else
            {
                var letterInfo = LettersInfo[PaintOptions.CursorPosition.Value];
                pos1.X = letterInfo.PositionX;
                pos1.Y = letterInfo.PositionY;
                pos2.X = pos1.X;
                pos2.Y = letterInfo.PositionY + LineHeight;
            }

            canvas.DrawLine(pos1, pos2, TextPaint);
        }

        private static void DrawSelectionByStartEndLetter(SKCanvas canvas, LetterInfo startLetter, LetterInfo endLetter, bool afterEndLetter = false)
        {
            if (startLetter == null || endLetter == null)
                return;

            if (!startLetter.Valid || !endLetter.Valid)
                return;

            var startPosition = SKPoint.Empty;
            var endPosition = SKPoint.Empty;

            startPosition.X = startLetter.PositionX;
            startPosition.Y = startLetter.PositionY;

            endPosition.X = endLetter.PositionX;
            endPosition.Y = endLetter.PositionY;

            if (afterEndLetter)
            {
                float endLetterAdvanceX = 0;
                if (FontCache.GetLetterDefinitionForChar(endLetter.Character, out var letterDef))
                    endLetterAdvanceX = letterDef.AdvanceX;

                endPosition.X += endLetterAdvanceX;
            }

            canvas.DrawRect(startPosition.X, startPosition.Y, endPosition.X - startPosition.X, LineHeight, TextPaint);
        }

        private static LetterInfo EnsureValidLetter(int selectionIndex)
        {
            if (selectionIndex == Text.Length)
                return null;

            var letterInfo = LettersInfo[selectionIndex];

            while (!letterInfo.Valid)
            {
                selectionIndex++;

                letterInfo = LettersInfo[selectionIndex];

                if (selectionIndex == Text.Length)
                    break;
            }

            if (letterInfo.Valid)
                return letterInfo;

            return null;
        }

        private static LetterInfo EnsureValidEndLetter(int selectionIndex)
        {
            var letterInfo = LettersInfo[selectionIndex];

            while (!letterInfo.Valid)
            {
                selectionIndex--;

                letterInfo = LettersInfo[selectionIndex];

                if (selectionIndex == 0)
                    break;
            }

            if (letterInfo.Valid)
                return letterInfo;

            return null;
        }

        private static bool DrawSelectionIfNeed(SKCanvas canvas)
        {
            if (PaintOptions == null || PaintOptions.SelectionStart == null || PaintOptions.SelectionEnd == null)
                return false;

            if (PaintOptions.SelectionStart == PaintOptions.SelectionEnd)
                return false;

            TextPaint.Color = PaintOptions.SelectionColor;

            var startLetter = EnsureValidLetter(PaintOptions.SelectionStart.Value);
            var endLetter = EnsureValidLetter(PaintOptions.SelectionEnd.Value);

            bool afterEndLetter = false;
            if (PaintOptions.SelectionEnd.Value == Text.Length)
            {
                afterEndLetter = true;
                endLetter = EnsureValidEndLetter(PaintOptions.SelectionEnd.Value - 1);
            }

            if (startLetter.LineIndex == endLetter.LineIndex)
            {
                DrawSelectionByStartEndLetter(canvas, startLetter, endLetter, afterEndLetter);
                return true;
            }

            startLetter = null;
            for (int i = PaintOptions.SelectionStart.Value; i <= PaintOptions.SelectionEnd.Value; i++)
            {
                if (i == PaintOptions.SelectionEnd.Value)
                {
                    if (startLetter == null)
                        return true;

                    var prevLetter = EnsureValidEndLetter(i - 1);
                    DrawSelectionByStartEndLetter(canvas, startLetter, prevLetter, true);

                    return true;
                }

                var currentLetter = LettersInfo[i];

                if (startLetter == null && !currentLetter.Valid)
                    continue;

                if (startLetter == null)
                {
                    startLetter = currentLetter;
                    continue;
                }

                if ((currentLetter.Character == UnicodeCharacters.NewLine || currentLetter.Character == UnicodeCharacters.CarriageReturn) ||
                    currentLetter.LineIndex != startLetter.LineIndex)
                {
                    var prevLetter = EnsureValidEndLetter(i - 1);
                    DrawSelectionByStartEndLetter(canvas, startLetter, prevLetter, true);

                    startLetter = null;
                }
            }

            return true;
        }

        private static void DrawToCanvas(SKCanvas canvas, ref SKColor foreColor)
        {
            SKPoint[] glyphPositions = new SKPoint[Text.Length];

            var drawSelection = DrawSelectionIfNeed(canvas);

            // Draw underline/strikethrough and texts
            {
                TextPaint.Color = foreColor;

                if (TextStyle == FontStyle.Underline || TextStyle == FontStyle.Strikeout)
                    LinesHadDrawedUnderlines.Clear();

                for (int i = 0; i < Text.Length; i++)
                {
                    var letterInfo = LettersInfo[i];

                    if (!letterInfo.Valid)
                        continue;

                    // X and Y coordinates passed to the DrawText method specify the left side of the text at the baseline.
                    // So we need move Y with a ascender.
                    var realPosY = letterInfo.PositionY + FontCache.FontAscender;
                    glyphPositions[i] = new SKPoint(letterInfo.PositionX, realPosY);

                    if (TextStyle == FontStyle.Underline || TextStyle == FontStyle.Strikeout)
                    {
                        if (LinesHadDrawedUnderlines.Contains(letterInfo.LineIndex))
                            continue;

                        realPosY += TextStyle == FontStyle.Underline ? (TextPaint.FontMetrics.UnderlinePosition ?? 0) : (TextPaint.FontMetrics.StrikeoutPosition ?? 0);
                        canvas.DrawLine(new SKPoint(letterInfo.PositionX, realPosY), new SKPoint(letterInfo.PositionX + LinesWidth[letterInfo.LineIndex], realPosY), TextPaint);

                        LinesHadDrawedUnderlines.Add(letterInfo.LineIndex);
                    }
                }
                canvas.DrawPositionedText(Text, glyphPositions, TextPaint);
                //SKCanvas.DrawPositionedText(string, SKPoint[], SKPaint)' is obsolete: 
                //'Use DrawText(SKTextBlob, float, float, SKPaint) instead.'
            }

            if (drawSelection)
                TextPaint.Color = new SKColor(255, 135, 26);

            DrawCursorIfNeed(canvas);
        }

        public static void DrawText(SKCanvas canvas, string text, Font font, SKRect bounds, SKColor foreColor, TextFormatFlags flags, TextPaintOptions options)
        {
            if (string.IsNullOrEmpty(text))
            {
                if (options != null && options.CursorPosition != null)
                    DrawCursorForEmptyString(canvas, font, ref foreColor);

                return;
            }

            Text = text;
            Flags = flags;
            PrepareTextPaint(font);
            MaxLineWidth = bounds.Width - LeftPadding - RightPadding;
            Bounds = bounds;
            PaintOptions = options;

            if (PaintOptions != null)
                PaintOptions.EnsureSafeOptionValuesForText(Text);

            AlignText();

            ComputeAlignmentOffset();
            ComputeLetterPositionInBounds(ref bounds);

            DrawToCanvas(canvas, ref foreColor);
        }

        public static void DrawText(SKCanvas canvas, string text, Font font, SKRect bounds, SKColor foreColor, TextFormatFlags flags)
        {
            DrawText(canvas, text, font, bounds, foreColor, flags, null);
        }

        public static void DrawText(SKCanvas canvas, string text, Font font, SKRect bounds, SKColor foreColor, TextFormatFlags flags, int cursorPosition)
        {
            var options = new TextPaintOptions();
            options.CursorPosition = cursorPosition;

            DrawText(canvas, text, font, bounds, foreColor, flags, options);
        }

        public static SKPoint GetCursorDrawPosition(string text, Font font, SKRect bounds, TextFormatFlags flags, int cursorPosition)
        {
            if (string.IsNullOrEmpty(text))
                return new SKPoint(bounds.Left, bounds.Top);

            Text = text;
            Flags = flags;
            PrepareTextPaint(font);
            MaxLineWidth = bounds.Width - LeftPadding - RightPadding;
            Bounds = bounds;

            AlignText();

            ComputeAlignmentOffset();
            ComputeLetterPositionInBounds(ref bounds);

            if (cursorPosition <= 0)
                return new SKPoint(bounds.Left, bounds.Top);

            LetterInfo letterInfo;
            FontLetterDefinition letterDef;
            SKPoint pos = SKPoint.Empty;

            if (cursorPosition >= Text.Length)
            {
                cursorPosition = Text.Length;

                letterInfo = EnsureValidEndLetter(cursorPosition - 1);

                if (letterInfo == null)
                    return new SKPoint(bounds.Left, bounds.Top);

                FontCache.GetLetterDefinitionForChar(letterInfo.Character, out letterDef);
                pos.X = letterInfo.PositionX + letterDef.AdvanceX;
                pos.Y = letterInfo.PositionY;

                return pos;
            }

            letterInfo = EnsureValidLetter(cursorPosition);

            if (letterInfo == null)
                return new SKPoint(bounds.Left, bounds.Top);

            FontCache.GetLetterDefinitionForChar(letterInfo.Character, out letterDef);
            pos.X = letterInfo.PositionX;
            pos.Y = letterInfo.PositionY;

            return pos;
        }

        public static int GetCursorFromPoint(string text, Font font, SKRect bounds, TextFormatFlags flags, SKPoint point)
        {
            return GetCursorFromPoint(text, font, bounds, flags, point, out var drawPos);
        }

        public static int GetCursorFromPoint(string text, Font font, SKRect bounds, TextFormatFlags flags, SKPoint point, out SKPoint cursorDrawPosition)
        {
            cursorDrawPosition = SKPoint.Empty;

            if (string.IsNullOrEmpty(text))
                return 0;

            Text = text;
            Flags = flags;
            PrepareTextPaint(font);
            MaxLineWidth = bounds.Width - LeftPadding - RightPadding;
            Bounds = bounds;

            AlignText();

            ComputeAlignmentOffset();
            ComputeLetterPositionInBounds(ref bounds);

            int lineIndex = (int)(point.Y / LineHeight);

            for (int i = 0; i < Text.Length; i++)
            {
                var letterInfo = LettersInfo[i];
                if (letterInfo.LineIndex != lineIndex || !letterInfo.Valid)
                    continue;

                FontLetterDefinition letterDef;
                FontCache.GetLetterDefinitionForChar(letterInfo.Character, out letterDef);

                // Click the left side of the first character
                if (point.X <= letterInfo.PositionX)
                    return 0;

                if (point.X <= letterInfo.PositionX + letterDef.AdvanceX)
                {
                    if (point.X <= letterInfo.PositionX + letterDef.AdvanceX / 2)
                    {
                        cursorDrawPosition.X = letterInfo.PositionX;
                        cursorDrawPosition.Y = letterInfo.PositionY;
                        return i;
                    }
                    else
                    {
                        cursorDrawPosition.X = letterInfo.PositionX + letterDef.AdvanceX;
                        cursorDrawPosition.Y = letterInfo.PositionY;
                        return i + 1;
                    }
                }
                else
                {
                    if (i < Text.Length)
                    {
                        if (i == Text.Length - 1)
                        {
                            cursorDrawPosition.X = letterInfo.PositionX + letterDef.AdvanceX;
                            cursorDrawPosition.Y = letterInfo.PositionY;
                            return i + 1;
                        }
                        else
                            continue;
                    }
                }
            }

            return 0;
        }
    }
}
