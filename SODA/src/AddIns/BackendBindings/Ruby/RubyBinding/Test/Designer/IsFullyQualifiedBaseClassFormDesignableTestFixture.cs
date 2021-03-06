// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Matthew Ward" email="mrward@users.sourceforge.net"/>
//     <version>$Revision: 5343 $</version>
// </file>

using System;
using ICSharpCode.FormsDesigner;
using ICSharpCode.RubyBinding;
using ICSharpCode.SharpDevelop.Dom;
using NUnit.Framework;
using RubyBinding.Tests.Utils;

namespace RubyBinding.Tests.Designer
{
	/// <summary>
	/// Tests that the SecondaryDisplayBinding detects that the form
	/// is designable when the base class is fully qualified.
	/// </summary>
	[TestFixture]
	public class IsFullyQualifiedBaseClassFormDesignableTestFixture : IsFormDesignableTestFixture
	{						
		protected override string GetRubyCode()
		{
			return "require \"System.Windows.Forms\"\r\n" +
					"\r\n" + 
					"class MainForm < System::Windows::Forms::Form\r\n" +
					"    def initialize()\r\n" +
					"        self.InitializeComponent()\r\n" +
					"    end\r\n" +
					"\r\n" +
					"    def InitializeComponent()\r\n" +
					"    end\r\n" +
					"end\r\n" +
					"\r\n";
		}
		
		[Test]
		public void MainFormClassBaseTypeIsForm()
		{
			IReturnType baseType = null;
			foreach (IReturnType returnType in mainFormClass.BaseTypes) {
				if (returnType.Name == "Form") {
					baseType = returnType;
					break;
				}
			}
			Assert.IsNotNull(baseType);
		}
		
		[Test]
		public void MainFormClassBaseTypeFullNameIsSystemWindowsFormsForm()
		{
			IReturnType baseType = null;
			foreach (IReturnType returnType in mainFormClass.BaseTypes) {
				if (returnType.FullyQualifiedName == "System.Windows.Forms.Form") {
					baseType = returnType;
					break;
				}
			}
			Assert.IsNotNull(baseType);
		}
	}
}
