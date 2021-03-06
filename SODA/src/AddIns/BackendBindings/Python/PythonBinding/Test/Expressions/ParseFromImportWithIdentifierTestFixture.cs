// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Matthew Ward" email="mrward@users.sourceforge.net"/>
//     <version>$Revision: 5377 $</version>
// </file>

using System;
using ICSharpCode.PythonBinding;
using NUnit.Framework;

namespace PythonBinding.Tests.Expressions
{
	[TestFixture]
	public class ParseFromImportWithIdentifierTestFixture
	{
		PythonImportExpression importExpression;
		
		[SetUp]
		public void Init()
		{
			string code = "from System import Console";
			importExpression = new PythonImportExpression(code);
		}
		
		[Test]
		public void HasImportAndFromReturnsTrue()
		{
			Assert.IsTrue(importExpression.HasFromAndImport);
		}
		
		[Test]
		public void ImportIdentifierIsConsole()
		{
			Assert.AreEqual("Console", importExpression.Identifier);
		}
		
		[Test]
		public void HasIdentifierReturnsTrue()
		{
			Assert.IsTrue(importExpression.HasIdentifier);
		}
	}
}
