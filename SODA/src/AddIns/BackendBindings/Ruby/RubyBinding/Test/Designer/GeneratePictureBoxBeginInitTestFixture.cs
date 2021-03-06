// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Matthew Ward" email="mrward@users.sourceforge.net"/>
//     <version>$Revision: 5343 $</version>
// </file>

using System;
using System.CodeDom;
using System.ComponentModel.Design.Serialization;
using System.Windows.Forms;
using ICSharpCode.RubyBinding;
using NUnit.Framework;
using RubyBinding.Tests.Utils;

namespace RubyBinding.Tests.Designer
{
	[TestFixture]
	public class GeneratePictureBoxBeginInitTestFixture
	{
		string generatedRubyCode;
		
		[TestFixtureSetUp]
		public void SetUpFixture()
		{
			RubyCodeDomSerializer serializer = new RubyCodeDomSerializer("    ");
			using (Form form = new Form()) {
				form.Site = new MockSite();
				CodeMemberMethod method = CreateMethod();
				DesignerSerializationManager serializationManager = new DesignerSerializationManager();
				using (serializationManager.CreateSession()) {
					generatedRubyCode = serializer.GenerateMethodBody(form, method, serializationManager, String.Empty, 0);
				}
			}
		}
		
		CodeMemberMethod CreateMethod()
		{
			CodeMemberMethod method = new CodeMemberMethod();
			
			// BeginInit method call.
			CodeExpressionStatement statement = new CodeExpressionStatement();
			CodeMethodInvokeExpression methodInvoke = new CodeMethodInvokeExpression();
			statement.Expression = methodInvoke;
			
			CodeMethodReferenceExpression methodRef = new CodeMethodReferenceExpression();
			methodRef.MethodName = "BeginInit";
			
			CodeCastExpression cast = new CodeCastExpression();
			cast.TargetType = new CodeTypeReference();
			cast.TargetType.BaseType = "System.ComponentModel.ISupportInitialize";
			
			CodeFieldReferenceExpression fieldRef = new CodeFieldReferenceExpression();
			fieldRef.FieldName = "pictureBox1";
			fieldRef.TargetObject = new CodeThisReferenceExpression();
			cast.Expression = fieldRef;

			methodRef.TargetObject = cast;
			methodInvoke.Method = methodRef;

			method.Statements.Add(statement);
			return method;
		}
		
		[Test]
		public void GeneratedCode()
		{
			string expectedCode =
				"@pictureBox1.clr_member(System::ComponentModel::ISupportInitialize, :BeginInit).call()\r\n";
			
			Assert.AreEqual(expectedCode, generatedRubyCode, generatedRubyCode);
		}
	}
}
