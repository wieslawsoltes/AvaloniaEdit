// Copyright (c) 2014 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using UnoEdit.Rendering;
using Microsoft.UI.Xaml.Media;
using Color = Windows.UI.Color;
using FontWeight = Windows.UI.Text.FontWeight;
using FontStyle = Windows.UI.Text.FontStyle;
using FontWeights = Microsoft.UI.Text.FontWeights;


namespace UnoEdit.Highlighting
{
	/// <summary>
	/// A brush used for syntax highlighting. Can retrieve a real brush on-demand.
	/// </summary>
	public abstract class HighlightingBrush
	{
		/// <summary>
		/// Gets the real brush.
		/// </summary>
		/// <param name="context">The construction context. context can be null!</param>
		public abstract Brush GetBrush(ITextRunConstructionContext context);
		
		/// <summary>
		/// Gets the color of the brush.
		/// </summary>
		/// <param name="context">The construction context. context can be null!</param>
		public virtual Color? GetColor(ITextRunConstructionContext context)
		{
		    if (GetBrush(context) is SolidColorBrush scb)
                return scb.Color;
		    return null;
		}
	}
	
	/// <summary>
	/// Highlighting brush implementation that takes a frozen brush.
	/// </summary>
	public sealed class SimpleHighlightingBrush : HighlightingBrush
	{
	    private readonly Color _color;
		
		internal SimpleHighlightingBrush(SolidColorBrush brush)
		{
			_color = brush?.Color ?? throw new System.ArgumentNullException(nameof(brush));
		}
		
		/// <summary>
		/// Creates a new HighlightingBrush with the specified color.
		/// </summary>
		public SimpleHighlightingBrush(Color color) { _color = color; }

        /// <inheritdoc/>
        public override Color? GetColor(ITextRunConstructionContext context) => _color;
		
		/// <inheritdoc/>
		public override Brush GetBrush(ITextRunConstructionContext context)
		{
			return new SolidColorBrush(_color);
		}

		/// <inheritdoc/>
		public override string ToString()
		{
			return $"#{_color.A:X2}{_color.R:X2}{_color.G:X2}{_color.B:X2}";
		}
		
		/// <inheritdoc/>
		public override bool Equals(object obj)
		{
			SimpleHighlightingBrush other = obj as SimpleHighlightingBrush;
			if (other == null)
				return false;
			return _color.Equals(other._color);
		}
		
		/// <inheritdoc/>
		public override int GetHashCode()
		{
			return _color.GetHashCode();
		}
	}
}
