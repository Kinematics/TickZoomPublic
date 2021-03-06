// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Matthew Ward" email="mrward@users.sourceforge.net"/>
//     <version>$Revision: 5392 $</version>
// </file>

using System;
using System.Collections;
using System.Collections.Generic;
using ICSharpCode.PythonBinding;
using ICSharpCode.SharpDevelop.Dom;
using ICSharpCode.SharpDevelop.Dom.CSharp;
using NUnit.Framework;
using PythonBinding.Tests.Utils;

namespace PythonBinding.Tests.Resolver
{
	[TestFixture]
	public class ResolveBuiltInRoundMethodTestFixture : ResolveTestFixtureBase
	{
		protected override ExpressionResult GetExpressionResult()
		{
			return new ExpressionResult("round", ExpressionContext.Default);
		}
		
		protected override string GetPythonScript()
		{
			return 
				"round\r\n" +
				"\r\n";
		}
			
		[Test]
		public void ResolveResultIsMethodGroupResolveResult()
		{
			Assert.IsTrue(resolveResult is MethodGroupResolveResult);
		}
		
		[Test]
		public void ResolveResultMethodNameIsRound()
		{
			Assert.AreEqual("round", MethodResolveResult.Name);
		}
		
		MethodGroupResolveResult MethodResolveResult {
			get { return (MethodGroupResolveResult)resolveResult; }
		}
		
		[Test]
		public void ResolveResultContainingTypeHasTwoRoundMethods()
		{
			List<IMethod> exitMethods = GetRoundMethods();
			Assert.AreEqual(2, exitMethods.Count);
		}
		
		List<IMethod> GetRoundMethods()
		{
			return GetRoundMethods(-1);
		}
		
		List<IMethod> GetRoundMethods(int parameterCount)
		{
			List<IMethod> methods = MethodResolveResult.ContainingType.GetMethods();
			return PythonCompletionItemsHelper.FindAllMethodsFromCollection("round", parameterCount, methods.ToArray());
		}
		
		[Test]
		public void BothRoundMethodsArePublic()
		{
			foreach (IMethod method in GetRoundMethods()) {
				Assert.IsTrue(method.IsPublic);
			}
		}
		
		[Test]
		public void BothRoundMethodsHaveClassWithNameOfSys()
		{
			foreach (IMethod method in GetRoundMethods()) {
				Assert.AreEqual("__builtin__", method.DeclaringType.Name);
			}
		}
		
		[Test]
		public void OneRoundMethodHasTwoParameters()
		{
			int parameterCount = 2;
			Assert.AreEqual(1, GetRoundMethods(parameterCount).Count);
		}
		
		[Test]
		public void RoundMethodParameterNameIsNumber()
		{
			IParameter parameter = GetFirstRoundMethodParameter();
			Assert.AreEqual("number", parameter.Name);
		}
		
		IParameter GetFirstRoundMethodParameter()
		{
			int parameterCount = 1;
			List<IMethod> methods = GetRoundMethods(parameterCount);
			IMethod method = methods[0];
			return method.Parameters[0];
		}
		
		[Test]
		public void RoundMethodParameterReturnTypeIsDouble()
		{
			IParameter parameter = GetFirstRoundMethodParameter();
			Assert.AreEqual("Double", parameter.ReturnType.Name);
		}
		
		[Test]
		public void RoundMethodParameterConvertedToStringUsingAmbienceReturnsDoubleNumberString()
		{
			IAmbience ambience = new CSharpAmbience();
			string text = ambience.Convert(GetFirstRoundMethodParameter());
			Assert.AreEqual("double number", text);
		}
	}
}
