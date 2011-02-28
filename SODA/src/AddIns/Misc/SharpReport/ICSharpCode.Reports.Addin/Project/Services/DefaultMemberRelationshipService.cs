/*
 * Erstellt mit SharpDevelop.
 * Benutzer: Peter Forstmeier
 * Datum: 21.10.2007
 * Zeit: 16:07
 * 
 * Sie k�nnen diese Vorlage unter Extras > Optionen > Codeerstellung > Standardheader �ndern.
 */
using System;
using System.ComponentModel.Design.Serialization;

namespace ICSharpCode.Reports.Addin
{
	/// <summary>
	/// Description of DefaultMemberRelationshipService.
	/// </summary>
	public class DefaultMemberRelationshipService:MemberRelationshipService
	{
		
		public DefaultMemberRelationshipService()
		{
		}
		public override bool SupportsRelationship(MemberRelationship source, MemberRelationship relationship)
		{
			return true;
		}
		protected override MemberRelationship GetRelationship(MemberRelationship source)
		{
			return base.GetRelationship(source);
		}
	}
}
