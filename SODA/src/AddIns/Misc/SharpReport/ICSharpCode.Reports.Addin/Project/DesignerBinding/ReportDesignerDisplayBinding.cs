/*
 * Erstellt mit SharpDevelop.
 * Benutzer: Peter Forstmeier
 * Datum: 03.10.2007
 * Zeit: 16:50
 * 
 * Sie k�nnen diese Vorlage unter Extras > Optionen > Codeerstellung > Standardheader �ndern.
 */

using System;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Gui;
using ICSharpCode.Reports.Addin.ReportWizard;
namespace ICSharpCode.Reports.Addin
{
	/// <summary>
	/// Description of ReportDesignerDisplayBinding.
	/// </summary>
	public class ReportDesignerDisplayBinding:IDisplayBinding
	{
		public ReportDesignerDisplayBinding()
		{
		}
		
		public bool CanCreateContentForFile(string fileName)
		{
			return System.IO.Path.GetExtension(fileName).Equals(".srd",StringComparison.OrdinalIgnoreCase) ;
		}
		
		
		public IViewContent CreateContentForFile(OpenedFile file)
		{
			if (file.IsDirty) {
				ReportWizardCommand cmd = new ReportWizardCommand(file);
				cmd.Run();
				if (cmd.Canceled) {
					return null;
				}
				file.SetData(cmd.GeneratedReport.ToArray());
			}
			ReportDesignerView view = ICSharpCode.Reports.Addin.Commands.StartViewCommand.SetupDesigner(file);
			return view;
		}
	}
}
