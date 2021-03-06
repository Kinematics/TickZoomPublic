// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Matthew Ward" email="mrward@users.sourceforge.net"/>
//     <version>$Revision: 4054 $</version>
// </file>

using System;
using System.IO;
using ICSharpCode.PythonBinding;
using ICSharpCode.SharpDevelop.Dom;
using ICSharpCode.SharpDevelop.Internal.Templates;
using ICSharpCode.SharpDevelop.Project;
using NUnit.Framework;
using PythonBinding.Tests;
using PythonBinding.Tests.Utils;

namespace PythonBinding.Tests.Parsing
{
	/// <summary>
	/// Tests the PythonParser.
	/// </summary>
	[TestFixture]
	public class ParserTestFixture
	{
		PythonParser parser;
		
		[TestFixtureSetUp]
		public void SetUpFixture()
		{
			MSBuildEngineHelper.InitMSBuildEngine();
			parser = new PythonParser();
		}
		
		[Test]
		public void CanParsePythonFileName()
		{
			Assert.IsTrue(parser.CanParse("test.py"));
		}

		[Test]
		public void CanParseTextFileName()
		{
			Assert.IsFalse(parser.CanParse("test.txt"));
		}
		
		[Test]
		public void CanParseNullFileName()
		{
			string fileName = null;
			Assert.IsFalse(parser.CanParse(fileName));
		}
		
		[Test]
		public void CanParseUpperCasePythonFileName()
		{
			Assert.IsTrue(parser.CanParse("TEST.PY"));
		}
		
		[Test]
		public void NoLexerTags()
		{
			Assert.AreEqual(0, parser.LexerTags.Length);
		}
		
		[Test]
		public void SetLexerTags()
		{
			PythonParser parser = new PythonParser();
			string[] tags = new string[] {"Test"};
			parser.LexerTags = tags;
			
			Assert.AreEqual(tags, parser.LexerTags);
		}
		
		[Test]
		public void LanguageProperties()
		{
			Assert.IsNotNull(parser.Language);
		}
		
		[Test]
		public void CannotParseNullProject()
		{
			IProject project = null;
			Assert.IsFalse(parser.CanParse(project));
		}
		
		[Test]
		public void CanParsePythonProject()
		{
			ProjectCreateInformation createInfo = new ProjectCreateInformation();
			createInfo.Solution = new Solution();
			createInfo.OutputProjectFileName = @"C:\projects\test.pyproj";
			PythonProject project = new PythonProject(createInfo);
			Assert.IsTrue(parser.CanParse(project));
		}
	
		[Test]
		public void CannotParseNonPythonLanguageProject()
		{
			MockProject project = new MockProject();
			project.Language = "Test";
			Assert.IsFalse(parser.CanParse(project));
		}
		
		[Test]
		public void ParseWithNullFileContent()
		{
			DefaultProjectContent projectContent = new DefaultProjectContent();
			ICompilationUnit unit = parser.Parse(projectContent, null, null);
			Assert.IsInstanceOf(typeof(DefaultCompilationUnit), unit);
		}
		
		[Test]
		public void PythonExpressionFinderCreated()
		{
			IExpressionFinder expressionFinder = parser.CreateExpressionFinder(@"c:\Projects\test.py");
			Assert.IsInstanceOf(typeof(PythonExpressionFinder), expressionFinder);
		}
		
		[Test]
		public void Resolver()
		{
			Assert.IsInstanceOf(typeof(PythonResolver), parser.CreateResolver());
		}
	}
}
