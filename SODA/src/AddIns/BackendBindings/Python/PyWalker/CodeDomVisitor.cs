// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Matthew Ward" email="mrward@users.sourceforge.net"/>
//     <version>$Revision: 3727 $</version>
// </file>

using System;
using System.CodeDom;
using System.Collections;
using System.Reflection;
using System.Text;

namespace PyWalker
{
	/// <summary>
	/// Visits the code dom generated by PythonProvider.
	/// </summary>
	public class CodeDomVisitor
	{
		IOutputWriter writer;
			
		public CodeDomVisitor(IOutputWriter writer)
		{
			this.writer = writer;
		}
		
		public void Visit(CodeCompileUnit unit)
		{
			VisitCodeCompileUnit(unit);
		}
		
		void VisitCodeCompileUnit(CodeCompileUnit unit)
		{
			WriteLine("VisitCodeCompileUnit");
			
			foreach (CodeNamespace ns in unit.Namespaces) {
				VisitCodeNamespace(ns);
			}
		}
		
		void VisitCodeNamespace(CodeNamespace ns)
		{
			WriteLine("VisitCodeNamespace: " + ns.Name);
			
			foreach (CodeNamespaceImport import in ns.Imports) {
				VisitCodeNamespaceImport(import);
			}
			
			using (IDisposable indentLevel = Indentation.IncrementLevel()) {
				foreach (CodeTypeDeclaration type in ns.Types) {
					VisitCodeTypeDeclaration(type);
				}
			}
		}
		
		void VisitCodeNamespaceImport(CodeNamespaceImport import)
		{
			WriteLine("VisitCodeNamespaceImport: " + import.Namespace);
		}
		
		void VisitCodeTypeDeclaration(CodeTypeDeclaration type)
		{
			WriteLine("VisitCodeTypeDeclaration: " + type.Name);
			WriteLine(MemberAttributesToString(type.Attributes));

			WriteLine("UserData: " + UserDataKeysToString(type.UserData));
			
			WriteLine("VisitCodeTypeDeclaration: Custom attributes");
			foreach (CodeAttributeDeclaration attributeDeclaration in type.CustomAttributes) {
				VisitCodeAttributeDeclaration(attributeDeclaration);
			}
			
			WriteLine("TypeAttributes: " + TypeAttributesToString(type.TypeAttributes));

			foreach (CodeTypeParameter parameter in type.TypeParameters) {
				VisitCodeTypeParameter(parameter);
			}
			
			using (IDisposable indentLevel = Indentation.IncrementLevel()) {
				foreach (CodeTypeMember member in type.Members) {
					CodeMemberMethod method = member as CodeMemberMethod;
					CodeMemberField field = member as CodeMemberField;
					if (method != null) {
						VisitCodeMemberMethod(method);
					} else if (field != null) {
						VisitCodeMemberField(field);
					} else {
						WriteLine("Unhandled type member: " + member.GetType().Name);
					}
				}
			}
		}
		
		void VisitCodeTypeParameter(CodeTypeParameter parameter)
		{
			WriteLine("VisitCodeTypeParameter: " + parameter.Name);
		}
		
		string TypeAttributesToString(TypeAttributes typeAttributes)
		{
			return typeAttributes.ToString();
		}
		
		void VisitCodeAttributeDeclaration(CodeAttributeDeclaration attributeDeclaration)
		{
			WriteLine("VisitCodeAttributeDeclaration: " + attributeDeclaration.Name);
		}
				
		void VisitCodeMemberMethod(CodeMemberMethod method)
		{
			WriteLine("VisitCodeMemberMethod: " + method.Name);
			WriteLine(MemberAttributesToString(method.Attributes));
			
			WriteLine("UserData: " + UserDataKeysToString(method.UserData));
			foreach (CodeParameterDeclarationExpression param in method.Parameters) {
				VisitCodeParameterDeclarationExpression(param);
			}
			
			using (IDisposable indentLevel = Indentation.IncrementLevel()) {
				WriteLine("Method.Statements.Count: " + method.Statements.Count);
				foreach (CodeStatement statement in method.Statements) {
					VisitCodeStatement(statement);
				}
			}
		}

		void VisitCodeStatement(CodeStatement statement)
		{
			WriteLine("VisitCodeStatement: " + statement.GetType().Name);
			CodeVariableDeclarationStatement codeVariableDeclarationStatement = statement as CodeVariableDeclarationStatement;
			CodeAssignStatement codeAssignStatement = statement as CodeAssignStatement;
			CodeConditionStatement codeConditionStatement = statement as CodeConditionStatement;
			CodeIterationStatement codeIterationStatement = statement as CodeIterationStatement;
			CodeExpressionStatement codeExpressionStatement = statement as CodeExpressionStatement;
			CodeTryCatchFinallyStatement codeTryCatchFinallyStatement = statement as CodeTryCatchFinallyStatement;
			if (codeVariableDeclarationStatement != null) {
				VisitCodeVariableDeclarationStatement(codeVariableDeclarationStatement);
			} else if (codeAssignStatement != null) {
				VisitCodeAssignStatement(codeAssignStatement);
			} else if (codeConditionStatement != null) {
				VisitCodeConditionStatement(codeConditionStatement);
			} else if (codeIterationStatement != null) {
				VisitCodeIterationStatement(codeIterationStatement);
			} else if (codeExpressionStatement != null) {
				VisitCodeExpressionStatement(codeExpressionStatement);
			} else if (codeTryCatchFinallyStatement != null) {
				VisitCodeTryCatchFinallyStatement(codeTryCatchFinallyStatement);
			} else {
				WriteLine("Unhandled statement: " + statement.GetType().Name);
			}
		}
		
		void VisitCodeAssignStatement(CodeAssignStatement assignStatement)
		{
			WriteLine("VisitCodeAssignmentStatement");
			WriteLine("Left follows");
			VisitCodeExpression(assignStatement.Left);
			WriteLine("Right follows");
			VisitCodeExpression(assignStatement.Right);
		}
		
		void VisitCodeParameterDeclarationExpression(CodeParameterDeclarationExpression expression)
		{
			WriteLine("VisitCodeParameterDeclarationExpression: " + expression.Name);
			WriteLine("BaseType: " + expression.Type.BaseType);
		}
		
		void VisitCodeVariableDeclarationStatement(CodeVariableDeclarationStatement codeVariableDeclarationStatement)
		{
			WriteLine("VisitCodeVariableDeclarationStatement: " + codeVariableDeclarationStatement.Name);
			WriteLine("BaseType: " + codeVariableDeclarationStatement.Type.BaseType);		
			WriteLine("UserData: " + UserDataKeysToString(codeVariableDeclarationStatement.UserData));
			WriteLine("InitExpression follows");
			VisitCodeExpression(codeVariableDeclarationStatement.InitExpression);
		}
		
		void VisitCodeExpression(CodeExpression expression)
		{
			if (expression != null) {
				WriteLine("VisitCodeExpression: " + expression.GetType().Name);
				CodePrimitiveExpression primitiveExpression = expression as CodePrimitiveExpression;
				CodeFieldReferenceExpression fieldReferenceExpression = expression as CodeFieldReferenceExpression;
				CodeThisReferenceExpression thisReferenceExpression = expression as CodeThisReferenceExpression;
				CodeObjectCreateExpression createExpression = expression as CodeObjectCreateExpression;
				CodeBinaryOperatorExpression binaryExpression = expression as CodeBinaryOperatorExpression;
				CodeMethodReferenceExpression methodReferenceExpression = expression as CodeMethodReferenceExpression;
				CodeMethodInvokeExpression methodInvokeExpression = expression as CodeMethodInvokeExpression;
				CodeVariableReferenceExpression variableReferenceExpression = expression as CodeVariableReferenceExpression;
				if (primitiveExpression != null) {
					VisitCodePrimitiveExpression(primitiveExpression);
				} else if (fieldReferenceExpression != null) {
					VisitCodeFieldReferenceExpression(fieldReferenceExpression);
				} else if (thisReferenceExpression != null) {
					VisitCodeThisReferenceExpression(thisReferenceExpression);
				} else if (createExpression != null) {
					VisitObjectCreateExpression(createExpression);
				} else if (binaryExpression != null) {
					VisitCodeBinaryOperatorExpression(binaryExpression);
				} else if (methodReferenceExpression != null) {
					VisitCodeMethodReferenceExpression(methodReferenceExpression);
				} else if (methodInvokeExpression != null) {
					VisitCodeMethodInvokeExpression(methodInvokeExpression);
				} else if (variableReferenceExpression != null) {
					VisitCodeVariableReferenceExpression(variableReferenceExpression);
				}
			} else {
				WriteLine("VisitCodeExpression: Null");
			}
		}
		
		void VisitCodePrimitiveExpression(CodePrimitiveExpression expression)
		{
			WriteLine("VisitCodePrimitiveExpression: " + expression.Value);
		}
		
		void VisitCodeFieldReferenceExpression(CodeFieldReferenceExpression expression)
		{
			WriteLine("VisitFieldReferenceExpression: " + expression.FieldName);
			WriteLine("Target object follows");
			VisitCodeExpression(expression.TargetObject);
		}
		
		void VisitCodeThisReferenceExpression(CodeThisReferenceExpression expression)
		{
			WriteLine("VisitCodeThisReferenceExpression");
			WriteLine("UserData: " + UserDataKeysToString(expression.UserData));
		}
		
		void VisitCodeMemberField(CodeMemberField field)
		{
			WriteLine("VisitCodeMemberField: " + field.Name);
			WriteLine("UserData: " + UserDataKeysToString(field.UserData));
			WriteLine(MemberAttributesToString(field.Attributes));
			WriteLine("InitExpression follows");
			VisitCodeExpression(field.InitExpression);
		}
		
		void VisitObjectCreateExpression(CodeObjectCreateExpression createExpression)
		{
			WriteLine("VisitObjectCreateExpression: Type: " + createExpression.CreateType.BaseType);
			foreach (CodeExpression expression in createExpression.Parameters) {
				VisitCodeExpression(expression);
			}
		}

		void VisitCodeConditionStatement(CodeConditionStatement conditionStatement)
		{
			WriteLine("VisitCodeConditionStatement");
			
			WriteLine("Condition follows");
			using (IDisposable indentLevel = Indentation.IncrementLevel()) {
				VisitCodeExpression(conditionStatement.Condition);
			}
			
			WriteLine("TrueStatements follow");
			using (IDisposable indentLevel = Indentation.IncrementLevel()) {
				foreach (CodeStatement statement in conditionStatement.TrueStatements) {
					VisitCodeStatement(statement);
				}
			}
			
			WriteLine("FalseStatements follow");
			using (IDisposable indentLevel = Indentation.IncrementLevel()) {
				foreach (CodeStatement statement in conditionStatement.FalseStatements) {
					VisitCodeStatement(statement);
				}
			}			
		}
		
		void VisitCodeBinaryOperatorExpression(CodeBinaryOperatorExpression expression)
		{
			WriteLine("VisitBinaryOperatorExpression: " + expression.Operator);
			
			WriteLine("Left follows");
			using (IDisposable currentLevel = Indentation.IncrementLevel()) {
				VisitCodeExpression(expression.Left);
			}
			
			WriteLine("Right follows");
			using (IDisposable currentLevel = Indentation.IncrementLevel()) {
				VisitCodeExpression(expression.Right);
			}	
		}
		
		void VisitCodeIterationStatement(CodeIterationStatement statement)
		{
			WriteLine("VisitIterationStatement");

			WriteLine("Init statement follows");
			using (IDisposable currentLevel = Indentation.IncrementLevel()) {			
				VisitCodeStatement(statement.InitStatement);
			}
			
			WriteLine("Increment statement follows");
			using (IDisposable currentLevel = Indentation.IncrementLevel()) {			
				VisitCodeStatement(statement.IncrementStatement);
			}
		
			WriteLine("Test expression follows");
			using (IDisposable currentLevel = Indentation.IncrementLevel()) {			
				VisitCodeExpression(statement.TestExpression);
			}
				
			WriteLine("Statements follow");
			using (IDisposable currentLevel = Indentation.IncrementLevel()) {
				foreach (CodeStatement currentStatement in statement.Statements) {
					VisitCodeStatement(currentStatement);
				}
			}
		}

		void VisitCodeMethodInvokeExpression(CodeMethodInvokeExpression expression)
		{
			WriteLine("VisitCodeMethodInvokeExpression");
			using (IDisposable currentLevel = Indentation.IncrementLevel()) {
				VisitCodeExpression(expression.Method);
			}
		}

		void VisitCodeMethodReferenceExpression(CodeMethodReferenceExpression expression)
		{
			WriteLine("VisitCodeMethodReferenceExpression: " + expression.MethodName);
			WriteLine("Target Object follows");
			using (IDisposable currentLevel = Indentation.IncrementLevel()) {
				VisitCodeExpression(expression.TargetObject);
			}
		}

		void VisitCodeExpressionStatement(CodeExpressionStatement statement)
		{
			WriteLine("VisitCodeExpressionStatement");
			using (IDisposable currentLevel = Indentation.IncrementLevel()) {
				VisitCodeExpression(statement.Expression);
			}
		}
		
		void VisitCodeVariableReferenceExpression(CodeVariableReferenceExpression expression)
		{
			WriteLine("VisitCodeVariableReferenceExpression: " + expression.VariableName);
		}
		
		void VisitCodeTryCatchFinallyStatement(CodeTryCatchFinallyStatement tryStatement)
		{
			WriteLine("VisitCodeTryCatchFinallyStatement");
			using (IDisposable currentLevel = Indentation.IncrementLevel()) {
				WriteLine("Try statements follow: Count: " + tryStatement.TryStatements.Count);
				foreach (CodeStatement statement in tryStatement.TryStatements) {
					VisitCodeStatement(statement);
				}
				
				WriteLine("Catch clauses follow: Count: " + tryStatement.CatchClauses.Count);
				foreach (CodeCatchClause catchClause in tryStatement.CatchClauses) {
					VisitCodeCatchClause(catchClause);
				}				
				
				WriteLine("Finally statements follow: Count: " + tryStatement.FinallyStatements);
				foreach (CodeStatement statement in tryStatement.FinallyStatements) {
					VisitCodeStatement(statement);
				}				
			}
		}
		
		void VisitCodeCatchClause(CodeCatchClause catchClause)
		{
			WriteLine("VisitCodeCatchClause");
			WriteLine("Exception caught: " + catchClause.CatchExceptionType.BaseType);
			WriteLine("Exception variable: " + catchClause.LocalName);
			
			WriteLine("Catch statements follow: Count: " + catchClause.Statements.Count);
			using (IDisposable currentLevel = Indentation.IncrementLevel()) {
				foreach (CodeStatement statement in catchClause.Statements) {
					VisitCodeStatement(statement);
				}
			}
		}
		
		string MemberAttributesToString(MemberAttributes attributes)
		{
			StringBuilder s = new StringBuilder();
			s.Append("Attributes: ");
				
			if ((attributes & MemberAttributes.Public) == MemberAttributes.Public) {
				s.Append("Public, ");
			}
			if ((attributes & MemberAttributes.Private) == MemberAttributes.Private) {
				s.Append("Private, ");
			}
			if ((attributes & MemberAttributes.Family) == MemberAttributes.Family) {
				s.Append("Family, ");
			}
			if ((attributes & MemberAttributes.Final) == MemberAttributes.Final) {
				s.Append("Final, ");
			}

			return s.ToString();
		}
		
		string UserDataKeysToString(IDictionary userData)
		{
			StringBuilder s = new StringBuilder();			
			ICollection keys = userData.Keys;
			foreach (object o in keys) {
				string name = o as string;
				if (name != null) {
					s.Append(name);
					s.Append(", ");
				}
			}
			return s.ToString();
		}
		
		/// <summary>
		/// Writes a line and indents it to the current level.
		/// </summary>
		void WriteLine(string s)
		{
			writer.WriteLine(GetIndent() + s);
		}
		
		string GetIndent()
		{
			StringBuilder indent = new StringBuilder();
			for (int i = 0; i < Indentation.CurrentLevel; ++i) {
				indent.Append('\t');
			}
			return indent.ToString();
		}
	}
}
