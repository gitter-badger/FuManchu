﻿namespace FuManchu.Parser
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using FuManchu.Parser.SyntaxTree;
	using FuManchu.Tokenizer;

	/// <summary>
	/// Provides parsing services for the Handlebars language.
	/// </summary>
	public class HandlebarsParser : TokenizerBackedParser<HandlebarsTokenizer, HandlebarsSymbol, HandlebarsSymbolType>
	{
		private readonly IDictionary<HandlebarsSymbolType, HandlebarsSymbolType> _balancingSymbols = new Dictionary<HandlebarsSymbolType, HandlebarsSymbolType>
		{
			{ HandlebarsSymbolType.OpenTag, HandlebarsSymbolType.CloseTag },
			{ HandlebarsSymbolType.RawOpenTag, HandlebarsSymbolType.RawCloseTag },
			{ HandlebarsSymbolType.LeftBrace, HandlebarsSymbolType.RightBrace },
			{ HandlebarsSymbolType.RightBrace, HandlebarsSymbolType.RightBracket }
		};

		/// <inheritdoc />
		protected override LanguageCharacteristics<HandlebarsTokenizer, HandlebarsSymbol, HandlebarsSymbolType> Language
		{
			get { return HandlebarsLanguageCharacteristics.Instance; }
		}

		/// <summary>
		/// Parses a block tag.
		/// </summary>
		public void AtBlockTag()
		{
			var parent = Context.CurrentBlock;

			string tagName = null;
			// Start a new block.
			Context.StartBlock(BlockType.Tag);

			using (Context.StartBlock(BlockType.TagElement))
			{
				// Accept the open tag.
				AcceptAndMoveNext();
				// Output that tag as metacode.
				Output(SpanKind.MetaCode);

				if (Optional(HandlebarsSymbolType.Tilde))
				{
					// Output the tilde.
					Output(SpanKind.MetaCode);
				}

				// Accept the hash tag.
				AcceptAndMoveNext();
				// Output that tag as metacode.
				Output(SpanKind.MetaCode);

				// Accept everything until either the close of the tag, or the first element of whitespace.
				AcceptUntil(HandlebarsSymbolType.WhiteSpace, HandlebarsSymbolType.CloseTag, HandlebarsSymbolType.RawCloseTag);
				// Output the first part as an expression.
				Output(SpanKind.Expression);

				// Get the tag name and set it for the block.
				tagName = LastSpanContent();

				while (CurrentSymbol.Type != HandlebarsSymbolType.CloseTag && CurrentSymbol.Type != HandlebarsSymbolType.RawCloseTag && CurrentSymbol.Type != HandlebarsSymbolType.Tilde)
				{
					// Accept all the whitespace.
					AcceptAll(HandlebarsSymbolType.WhiteSpace);
					// Take all the whitespace, and output that.
					Output(SpanKind.WhiteSpace);

					if (CurrentSymbol.Type == HandlebarsSymbolType.Identifier || CurrentSymbol.Type == HandlebarsSymbolType.Keyword)
					{
						// We're at an identifier or keyword.
						AcceptAndMoveNext();
						if (CurrentSymbol.Type == HandlebarsSymbolType.Assign)
						{
							// We're in a parameterised argument (e.g. one=two
							AcceptAndMoveNext();
							// Accept everything until the next whitespace or closing tag.
							AcceptUntil(HandlebarsSymbolType.WhiteSpace, HandlebarsSymbolType.CloseTag, HandlebarsSymbolType.RawCloseTag, HandlebarsSymbolType.Tilde);
							// Output this as a map.
							Output(SpanKind.Map);
						}
						else if (CurrentSymbol.Type == HandlebarsSymbolType.WhiteSpace || CurrentSymbol.Type == HandlebarsSymbolType.CloseTag || CurrentSymbol.Type == HandlebarsSymbolType.RawCloseTag || CurrentSymbol.Type == HandlebarsSymbolType.Tilde)
						{
							// We're at a single parameter.
							Output(SpanKind.Parameter);
						}
					}
				}

				if (Optional(HandlebarsSymbolType.Tilde))
				{
					// Output the tilde.
					Output(SpanKind.MetaCode);
				}

				// Accept the closing tag.
				AcceptAndMoveNext();
				// Output this as metacode.
				Output(SpanKind.MetaCode);
			}

			// Special case, as we need to handle branching, so let's merge the last block into the parent block.
			if (tagName == "elseif" && parent != null && parent.Name == "if")
			{
				// Let's merge the current block with the parent and re-instate it.
				Context.MergeCurrentWithParent();
			}
			else
			{
				Context.CurrentBlock.Name = tagName;	
			}

			// Switch back to parsing the content of the block.
			ParseBlock();
		}

		/// <summary>
		/// Parses the end of a tag block.
		/// </summary>
		public void AtBlockEndTag()
		{
			string tagName = Context.CurrentBlock.Name;

			using (Context.StartBlock(BlockType.TagElement))
			{
				// Accept the open tag.
				AcceptAndMoveNext();
				// Output that tag as metacode.
				Output(SpanKind.MetaCode);

				if (Optional(HandlebarsSymbolType.Tilde))
				{
					// Output the tilde.
					Output(SpanKind.MetaCode);
				}

				// Accept the slash tag.
				AcceptAndMoveNext();
				// Output that tag as metacode.
				Output(SpanKind.MetaCode);

				// Accept everything until either the close of the tag.
				AcceptUntil(HandlebarsSymbolType.CloseTag, HandlebarsSymbolType.RawCloseTag, HandlebarsSymbolType.Tilde);
				// Output the first part as an expression.
				Output(SpanKind.Expression);

				// Get the name of the tag.
				string name = LastSpanContent();
				if (!string.Equals(name, tagName))
				{
					Context.OnError(CurrentLocation, "Unbalanced tags - expected a closing tag for '" + Context.CurrentBlock.Name + "' but instead found '" + name + "'");
				}

				if (Optional(HandlebarsSymbolType.Tilde))
				{
					// Output the tilde.
					Output(SpanKind.MetaCode);
				}

				// Accept the closing tag.
				AcceptAndMoveNext();
				// Output this as metacode.
				Output(SpanKind.MetaCode);
			}

			// End the current block;
			Context.EndBlock();
		}

		/// <summary>
		/// Parses an expression.
		/// </summary>
		public void AtExpressionTag()
		{
			string tagName = Context.CurrentBlock.Name;

			using (var block = Context.StartBlock(BlockType.Expression))
			{
				// Accept the open tag.
				AcceptAndMoveNext();
				// Output that tag as metacode.
				Output(SpanKind.MetaCode);

				if (Optional(HandlebarsSymbolType.Tilde))
				{
					// Output the tilde.
					Output(SpanKind.MetaCode);
				}

				// Accept everything until either the close of the tag, or the first element of whitespace.
				AcceptUntil(HandlebarsSymbolType.WhiteSpace, HandlebarsSymbolType.CloseTag, HandlebarsSymbolType.RawCloseTag, HandlebarsSymbolType.Tilde);
				// Output the first part as an expression.
				Output(SpanKind.Expression);

				string name = LastSpanContent();
				if (tagName == "if" && name == "else")
				{
					// Change the tag type to ensure this is mathed as a tag element.
					Context.CurrentBlock.Type = BlockType.TagElement;
				}

				while (CurrentSymbol.Type != HandlebarsSymbolType.CloseTag && CurrentSymbol.Type != HandlebarsSymbolType.RawCloseTag && CurrentSymbol.Type != HandlebarsSymbolType.Tilde)
				{
					// Accept all the whitespace.
					AcceptAll(HandlebarsSymbolType.WhiteSpace);
					// Take all the whitespace, and output that.
					Output(SpanKind.WhiteSpace);

					if (CurrentSymbol.Type == HandlebarsSymbolType.Identifier || CurrentSymbol.Type == HandlebarsSymbolType.Keyword)
					{
						// We're at an identifier or keyword.
						AcceptAndMoveNext();
						if (CurrentSymbol.Type == HandlebarsSymbolType.Assign)
						{
							// We're in a parameterised argument (e.g. one=two
							AcceptAndMoveNext();
							// Accept everything until the next whitespace or closing tag.
							AcceptUntil(HandlebarsSymbolType.WhiteSpace, HandlebarsSymbolType.CloseTag, HandlebarsSymbolType.RawCloseTag, HandlebarsSymbolType.Tilde);
							// Output this as a map.
							Output(SpanKind.Map);
						}
						else if (CurrentSymbol.Type == HandlebarsSymbolType.WhiteSpace || CurrentSymbol.Type == HandlebarsSymbolType.CloseTag || CurrentSymbol.Type == HandlebarsSymbolType.RawCloseTag || CurrentSymbol.Type == HandlebarsSymbolType.Tilde)
						{
							// We're at a single parameter.
							Output(SpanKind.Parameter);
						}
					}
				}

				if (Optional(HandlebarsSymbolType.Tilde))
				{
					// Output the tilde.
					Output(SpanKind.MetaCode);
				}

				// Accept the closing tag.
				AcceptAndMoveNext();
				// Output this as metacode.
				Output(SpanKind.MetaCode);
			}
		}

		/// <summary>
		/// Parses a tag.
		/// </summary>
		public void AtTag()
		{
			var current = CurrentSymbol;
			NextToken();

			if (CurrentSymbol.Type == HandlebarsSymbolType.Hash)
			{
				// Put the opening tag back.
				PutBack(CurrentSymbol);
				PutBack(current);
				NextToken();
				// We're at a block tag {{#hello}} etc.
				AtBlockTag();
			}
			else if (CurrentSymbol.Type == HandlebarsSymbolType.Bang)
			{
				// We're at a comment {{!....}}

			}
			else if (CurrentSymbol.Type == HandlebarsSymbolType.Slash)
			{
				// Put the opening tag back.
				PutBack(CurrentSymbol);
				PutBack(current);
				NextToken();
				// We're at a closing block tag {{/each}}
				AtBlockEndTag();
			}
			else
			{
				// Put the opening tag back.
				PutBack(CurrentSymbol);
				PutBack(current);
				NextToken();
				// Handle an expression tag.
				AtExpressionTag();
			}
		}

		/// <summary>
		/// Gets the content of the last span.
		/// </summary>
		/// <returns>The span content.</returns>
		private string LastSpanContent()
		{
			var span = Context.CurrentBlock.Children.LastOrDefault() as Span;
			if (span != null)
			{
				return span.Content;
			}
			return null;
		}

		/// <inheritdoc />
		public override void ParseBlock()
		{
			// Accept everything until we meet a tag (either {{ or {{{).
			AcceptUntil(HandlebarsSymbolType.OpenTag, HandlebarsSymbolType.RawOpenTag);

			// Output everything we have so far as text.
			Output(SpanKind.Text);

			if (EndOfFile || CurrentSymbol == null)
			{
				return;
			}

			if (CurrentSymbol.Type == HandlebarsSymbolType.OpenTag || CurrentSymbol.Type == HandlebarsSymbolType.RawOpenTag)
			{
				// Now we're at a tag.
//				AtTag();
			}
		}

		/// <inheritdoc />
		public override void ParseDocument()
		{
			using (PushSpanConfig())
			{
				if (Context == null)
				{
					throw new InvalidOperationException("Context has not been set.");
				}

				using (Context.StartBlock(BlockType.Text))
				{
					if (!NextToken())
					{
						return;
					}

					while (!EndOfFile)
					{
						ParseBlock();
					}
				}
			}
		}
	}
}