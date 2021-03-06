// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Matthew Ward" email="mrward@users.sourceforge.net"/>
//     <version>$Revision: 3738 $</version>
// </file>

using System;
using System.IO;

using ICSharpCode.FormsDesigner;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.DefaultEditor.Gui.Editor;
using ICSharpCode.SharpDevelop.Dom;
using ICSharpCode.SharpDevelop.Gui;
using ICSharpCode.TextEditor;
using ICSharpCode.TextEditor.Document;

namespace ICSharpCode.PythonBinding
{
	/// <summary>
	/// Forms designer display binding for Python files.
	/// </summary>
	public class PythonFormsDesignerDisplayBinding : ISecondaryDisplayBinding
	{
		public PythonFormsDesignerDisplayBinding()
		{
		}
		
		/// <summary>
		/// Returns true so that the CreateSecondaryViewContent method
		/// is called after the LoadSolutionProjects thread has finished.
		/// </summary>
		public bool ReattachWhenParserServiceIsReady {
			get { return true; }
		}
		
		public bool CanAttachTo(IViewContent content)
		{
			ITextEditorControlProvider textEditorControlProvider = content as ITextEditorControlProvider;
			if (textEditorControlProvider != null) {
				if (IsPythonFile(content.PrimaryFileName)) {
					ParseInformation parseInfo = GetParseInfo(content.PrimaryFileName, textEditorControlProvider.TextEditorControl.Text, false);
					return IsDesignable(parseInfo);
				}
			}
			return false;
		}

		public IViewContent[] CreateSecondaryViewContent(IViewContent viewContent)
		{
			return CreateSecondaryViewContent(viewContent, SharpDevelopTextEditorProperties.Instance);
		}
		
		public IViewContent[] CreateSecondaryViewContent(IViewContent viewContent, ITextEditorProperties textEditorProperties)
		{
			foreach (IViewContent existingView in viewContent.SecondaryViewContents) {
				if (existingView.GetType() == typeof(FormsDesignerViewContent)) {
					return new IViewContent[0];
				}
			}
			
			IDesignerLoaderProvider loader = new PythonDesignerLoaderProvider();
			IDesignerGenerator generator = new PythonDesignerGenerator(textEditorProperties);
			return new IViewContent[] { new FormsDesignerViewContent(viewContent, loader, generator) };
		}
		
		/// <summary>
		/// Gets the parse information from the parser service
		/// for the specified file.
		/// </summary>
		protected virtual ParseInformation GetParseInfo(string fileName, string textContent, bool updateCommentTags)
		{
			return ParserService.ParseFile(fileName, textContent, updateCommentTags);
		}
		
		/// <summary>
		/// Determines whether the specified parse information contains
		/// a class which is designable.
		/// </summary>
		protected virtual bool IsDesignable(ParseInformation parseInfo)
		{
			return FormsDesignerSecondaryDisplayBinding.IsDesignable(parseInfo);
		}
				
		/// <summary>
		/// Checks the file's extension represents a python file.
		/// </summary>
		static bool IsPythonFile(string fileName)
		{
			PythonParser parser = new PythonParser();
			return parser.CanParse(fileName);
		}
	}
}
