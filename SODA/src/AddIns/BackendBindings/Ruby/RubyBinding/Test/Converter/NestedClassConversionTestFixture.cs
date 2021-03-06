// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Matthew Ward" email="mrward@users.sourceforge.net"/>
//     <version>$Revision: 5343 $</version>
// </file>

using System;
using ICSharpCode.NRefactory;
using ICSharpCode.RubyBinding;
using NUnit.Framework;

namespace RubyBinding.Tests.Converter
{
	/// <summary>
	/// Tests that the indentation after the nested class is correct for any outer class methods.
	/// </summary>
	[TestFixture]	
	public class NestedClassConversionTestFixture
	{		
		string csharp =
			"class Foo\r\n" +
			"{\r\n" +
			"    public void Run()\r\n" +
			"    {\r\n" +
			"    }\r\n" +
			"\r\n" +
			"        class Bar\r\n" +
			"        {\r\n" +
			"            public void Test()\r\n" +
			"            {\r\n" +
			"            }\r\n" +
			"        }\r\n" +
			"\r\n" +
			"    public void AnotherRun()\r\n" +
			"    {\r\n" +
			"    }\r\n" +
			"}";
				
		[Test]
		public void ConvertedRubyCode()
		{
			NRefactoryToRubyConverter converter = new NRefactoryToRubyConverter(SupportedLanguage.CSharp);
			converter.IndentString = "    ";
			string ruby = converter.Convert(csharp);
			string expectedRuby =
				"class Foo\r\n" +
				"    def Run()\r\n" +
				"    end\r\n" +
				"\r\n" +
				"    class Bar\r\n" +
				"        def Test()\r\n" +
				"        end\r\n" +
				"    end\r\n" +
				"\r\n" +
				"    def AnotherRun()\r\n" +
				"    end\r\n" +
				"end";
			
			Assert.AreEqual(expectedRuby, ruby, ruby);
		}
	}
}

