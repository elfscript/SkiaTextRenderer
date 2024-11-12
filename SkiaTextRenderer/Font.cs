using SkiaSharp;

namespace SkiaTextRenderer
{
/*    public enum FontStyle
    {
        Regular,
        Bold,
        Italic,
        Underline,
        Strikeout
    }*/
/// 2024.11.09, cp from ../../PDFontLib_skia/EnumDef.cs
    [System.Flags]
    public enum FontStyle
    { 
      Regular=0, 
      Bold=1, 
      Italic=2, 
      Underline=4, 
      Strikeout=8,  
      Normal=Regular, 
      Semi=16, 
      Extra=32
    }
//======================

    public class Font
    {
        public SKTypeface Typeface { get; }
        public float Size { get; }
        public FontStyle Style { get; }

        public Font(SKTypeface typeface, float size, FontStyle style = FontStyle.Regular)
        {
            Typeface = typeface;
            Size = size;
            Style = style;
        }
    }
}
