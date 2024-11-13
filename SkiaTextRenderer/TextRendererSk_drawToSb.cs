using System;
using System.Text;
using System.Collections.Generic;
using SkiaSharp;

namespace SkiaTextRenderer
{
  public static partial class TextRendererSk
  {
    public static void DrawTextToSb(string text, Font font, SKRect bounds, 
        SKColor foreColor, TextFormatFlags flags, StringBuilder sb, Action<string, float, float, StringBuilder> pdtextAction)
    {
      Text = text;
      Flags = flags;
      PrepareTextPaint(font);
      MaxLineWidth = bounds.Width - LeftPadding - RightPadding;
      Bounds = bounds;

      AlignText();

      ComputeAlignmentOffset();
      ComputeLetterPositionInBounds(ref bounds);

      drawTextToSb(pdtextAction, ref foreColor, sb);
    }


    private static void drawTextToSb(Action<string, float, float, StringBuilder> pdtextAction, ref SKColor foreColor, StringBuilder sb)
    {
      // Draw underline/strikethrough and texts
      TextPaint.Color = foreColor;

      //if (TextStyle == FontStyle.Underline || TextStyle == FontStyle.Strikeout)
      //    LinesHadDrawedUnderlines.Clear();

      int lineIndex=0;
      int token1stCharIndex=0;
      StringBuilder sbtmp= new StringBuilder();

      for (int i = 0; i < Text.Length &&  lineIndex < NumberOfLines; i++)
      {
        var letterInfo = LettersInfo[i];

        if (!letterInfo.Valid)
          continue;

        // X and Y coordinates passed to the DrawText method specify the left side of the text at the baseline.
        // So we need move Y with a ascender.
        //var realPosY = letterInfo.PositionY + FontCache.FontAscender;

        /*if (TextStyle == FontStyle.Underline || TextStyle == FontStyle.Strikeout)
          {
          if (LinesHadDrawedUnderlines.Contains(letterInfo.LineIndex))
          continue;

          realPosY += TextStyle == FontStyle.Underline ? (TextPaint.FontMetrics.UnderlinePosition ?? 0) : (TextPaint.FontMetrics.StrikeoutPosition ?? 0);
          canvas.DrawLine(new SKPoint(letterInfo.PositionX, realPosY), new SKPoint(letterInfo.PositionX + LinesWidth[letterInfo.LineIndex], realPosY), TextPaint);

          LinesHadDrawedUnderlines.Add(letterInfo.LineIndex);
          }*/
        if(letterInfo.LineIndex == lineIndex){
          if(sbtmp.Length==0) token1stCharIndex=i;
          sbtmp.Append(letterInfo.Character);
        }
        else{
          if(sbtmp.Length > 0)  {
            float x= LettersInfo[token1stCharIndex].PositionX;
            //float y= lineIndex*LineHeight + FontCache.FontAscender;
            float y= LettersInfo[token1stCharIndex].PositionY + FontCache.FontAscender;

            pdtextAction(sbtmp.ToString(), x, Bounds.Height-y, sb);
            sbtmp.Clear();
          }

          do{ lineIndex++; } while(letterInfo.LineIndex > lineIndex);

          if(letterInfo.LineIndex == lineIndex){
            if(sbtmp.Length==0) token1stCharIndex=i;
            sbtmp.Append(letterInfo.Character);
          }
        }
      }

      //the last Line      
      if(sbtmp.Length > 0)  {
        float x= LettersInfo[token1stCharIndex].PositionX;
        //float y= (NumberOfLines-1)*LineHeight + FontCache.FontAscender;
        float y= LettersInfo[token1stCharIndex].PositionY + FontCache.FontAscender;
        pdtextAction(sbtmp.ToString(), x, Bounds.Height-y, sb);
        sbtmp.Clear();
      }
    }
    //==============================
  }
}
