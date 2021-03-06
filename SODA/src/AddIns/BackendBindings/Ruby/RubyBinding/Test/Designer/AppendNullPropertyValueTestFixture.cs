// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Matthew Ward" email="mrward@users.sourceforge.net"/>
//     <version>$Revision: 5343 $</version>
// </file>

using System;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.ComponentModel.Design.Serialization;
using System.Drawing;
using System.Windows.Forms;
using ICSharpCode.RubyBinding;
using NUnit.Framework;
using RubyBinding.Tests.Utils;

namespace RubyBinding.Tests.Designer
{
	/// <summary>
	/// Tests that a null property value does not cause a NullReferenceException.
	/// </summary>
	[TestFixture]
	public class AppendNullPropertyValueTestFixture
	{
		string generatedRubyCode;
		
		[TestFixtureSetUp]
		public void SetUpFixture()
		{
			using (DesignSurface designSurface = new DesignSurface(typeof(UserControl))) {
				IDesignerHost host = (IDesignerHost)designSurface.GetService(typeof(IDesignerHost));
				IEventBindingService eventBindingService = new MockEventBindingService(host);
				UserControl userControl = (UserControl)host.RootComponent;
				userControl.ClientSize = new Size(200, 300);
				
				NullPropertyUserControl control = (NullPropertyUserControl)host.CreateComponent(typeof(NullPropertyUserControl), "userControl1");
				control.Location = new Point(0, 0);
				control.Size = new Size(10, 10);
				userControl.Controls.Add(control);

				PropertyDescriptorCollection descriptors = TypeDescriptor.GetProperties(userControl);
				PropertyDescriptor namePropertyDescriptor = descriptors.Find("Name", false);
				namePropertyDescriptor.SetValue(userControl, "MainControl");
				
				DesignerSerializationManager serializationManager = new DesignerSerializationManager(host);
				using (serializationManager.CreateSession()) {
					RubyCodeDomSerializer serializer = new RubyCodeDomSerializer("    ");
					generatedRubyCode = serializer.GenerateInitializeComponentMethodBody(host, serializationManager);
				}
			}
		}
		
		[Test]
		public void GeneratedCode()
		{
			string expectedCode =
				"@userControl1 = RubyBinding::Tests::Utils::NullPropertyUserControl.new()\r\n" +
				"self.SuspendLayout()\r\n" +
				"# \r\n" +
				"# userControl1\r\n" +
				"# \r\n" +
				"@userControl1.FooBar = nil\r\n" +
				"@userControl1.Location = System::Drawing::Point.new(0, 0)\r\n" +
				"@userControl1.Name = \"userControl1\"\r\n" +
				"@userControl1.Size = System::Drawing::Size.new(10, 10)\r\n" +
				"@userControl1.TabIndex = 0\r\n" +
				"# \r\n" +
				"# MainControl\r\n" +
				"# \r\n" +
				"self.Controls.Add(@userControl1)\r\n" +
				"self.Name = \"MainControl\"\r\n" +
				"self.Size = System::Drawing::Size.new(200, 300)\r\n" +
				"self.ResumeLayout(false)\r\n";
			
			Assert.AreEqual(expectedCode, generatedRubyCode, generatedRubyCode);
		}		
	}
}
