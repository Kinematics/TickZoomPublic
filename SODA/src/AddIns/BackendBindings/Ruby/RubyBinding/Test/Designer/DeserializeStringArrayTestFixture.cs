// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Matthew Ward" email="mrward@users.sourceforge.net"/>
//     <version>$Revision: 5343 $</version>
// </file>

using System;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing;
using System.Windows.Forms;
using ICSharpCode.RubyBinding;
using IronRuby.Compiler.Ast;
using NUnit.Framework;
using RubyBinding.Tests.Utils;

namespace RubyBinding.Tests.Designer
{
	/// <summary>
	/// Tests that the string "System.Array" can be converted to an array.
	/// </summary>
	[TestFixture]
	public class DeserializeStringArrayTestFixture : DeserializeAssignmentTestFixtureBase
	{		
		public override string GetRubyCode()
		{
			return "self.Items = System::Array[System::String].new(\r\n" +
				"    [\"a\",\r\n" + 
				"    \"b\"])";
		}
		
		[Test]
		public void DeserializedObjectIsExpectedArray()
		{
			string[] expectedArray = new string[] {"a", "b"};
			Assert.AreEqual(expectedArray, deserializedObject);
		}
		
		[Test]
		public void StringTypeResolved()
		{
			Assert.AreEqual("System.String", componentCreator.LastTypeNameResolved);
		}
	}
}
