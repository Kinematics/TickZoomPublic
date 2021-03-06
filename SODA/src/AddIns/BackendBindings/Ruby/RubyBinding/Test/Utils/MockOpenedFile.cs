// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Matthew Ward" email="mrward@users.sourceforge.net"/>
//     <version>$Revision: 5343 $</version>
// </file>

using System;
using System.Collections.Generic;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Gui;

namespace RubyBinding.Tests.Utils
{
	/// <summary>
	/// Mock OpenedFile class.
	/// </summary>
	public class MockOpenedFile : OpenedFile
	{
		public MockOpenedFile()
		{
		}
		
		public MockOpenedFile(string fileName)
		{
			this.FileName = fileName;
		}
		
		public override IList<IViewContent> RegisteredViewContents {
			get {
				throw new NotImplementedException();
			}
		}
		
		public override void RegisterView(IViewContent view)
		{
		}
		
		public override void UnregisterView(IViewContent view)
		{
		}
	}
}
