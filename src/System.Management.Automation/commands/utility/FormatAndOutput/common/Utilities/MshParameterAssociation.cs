/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/
using System;
using System.Management.Automation;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Microsoft.PowerShell.Commands.Internal.Format
{
    /// <summary>
    /// helper class to hold a resolved expression and its
    /// originating parameter
    /// </summary>
    internal sealed class MshResolvedExpressionParameterAssociation
    {

        #region tracer
        [TraceSource ("MshResolvedExpressionParameterAssociation", "MshResolvedExpressionParameterAssociation")]
        internal static PSTraceSource tracer = PSTraceSource.GetTracer ("MshResolvedExpressionParameterAssociation", 
                                                "MshResolvedExpressionParameterAssociation");
        #endregion tracer

        internal MshResolvedExpressionParameterAssociation(MshParameter parameter, MshExpression expression)
        {
            if (expression == null)
                throw PSTraceSource.NewArgumentNullException ("expression");

            this._originatingParameter = parameter;
            this._resolvedExpression = expression;
        }

        internal MshExpression ResolvedExpression
        {
            get { return this._resolvedExpression; }
        }

        internal MshParameter OriginatingParameter
        {
            get { return this._originatingParameter; }
        }

        private MshExpression _resolvedExpression;
        private MshParameter _originatingParameter;
    }


    internal static class AssociationManager
    {
         internal static List<MshResolvedExpressionParameterAssociation> SetupActiveProperties (List<MshParameter> rawMshParameterList, 
                                                    PSObject target, MshExpressionFactory expressionFactory)
        {
            // check if we received properties from the command line
            if (rawMshParameterList != null && rawMshParameterList.Count > 0)
            {
                return AssociationManager.ExpandParameters (rawMshParameterList, target);
             }

            // we did not get any properties:
            //try to get properties from the default property set of the object
            List<MshResolvedExpressionParameterAssociation> activeAssociationList = AssociationManager.ExpandDefaultPropertySet(target, expressionFactory);

            if (activeAssociationList.Count > 0)
            {
                // we got a valid set of properties from the default property set..add computername for
                // remoteobjects (if available)
                if (PSObjectHelper.ShouldShowComputerNameProperty(target))
                {
                    activeAssociationList.Add(new MshResolvedExpressionParameterAssociation(null,
                        new MshExpression(RemotingConstants.ComputerNameNoteProperty)));
                }

                return activeAssociationList;
            }

            // we failed to get anything from the default property set
            // just get all the properties
            activeAssociationList = AssociationManager.ExpandAll (target);
            // Remove PSComputerName and PSShowComputerName from the display as needed.
            AssociationManager.HandleComputerNameProperties(target, activeAssociationList);           

            return activeAssociationList;
        }

        internal static List<MshResolvedExpressionParameterAssociation> ExpandTableParameters (List<MshParameter> parameters, PSObject target)
        {
            List<MshResolvedExpressionParameterAssociation> retVal = new List<MshResolvedExpressionParameterAssociation> ();

            foreach (MshParameter par in parameters)
            {
                MshExpression expression = par.GetEntry (FormatParameterDefinitionKeys.ExpressionEntryKey) as MshExpression;
                List<MshExpression> expandedExpressionList = expression.ResolveNames (target);

                if (!expression.HasWildCardCharacters && expandedExpressionList.Count == 0)
                {
                    // we did not find anything, mark as unresolved
                    retVal.Add (new MshResolvedExpressionParameterAssociation (par, expression));
                }

                foreach (MshExpression ex in expandedExpressionList)
                {
                    retVal.Add (new MshResolvedExpressionParameterAssociation (par, ex));
                }
            }

            return retVal;
        }

        internal static List<MshResolvedExpressionParameterAssociation> ExpandParameters (List<MshParameter> parameters, PSObject target)
        {
            List<MshResolvedExpressionParameterAssociation> retVal = new List<MshResolvedExpressionParameterAssociation> ();

            foreach (MshParameter par in parameters)
            {
                MshExpression expression = par.GetEntry (FormatParameterDefinitionKeys.ExpressionEntryKey) as MshExpression;
                List<MshExpression> expandedExpressionList = expression.ResolveNames (target);

                foreach (MshExpression ex in expandedExpressionList)
                {
                    retVal.Add (new MshResolvedExpressionParameterAssociation (par, ex));
                }
            }

            return retVal;
        }

        internal static List<MshResolvedExpressionParameterAssociation> ExpandDefaultPropertySet(PSObject target, MshExpressionFactory expressionFactory)
        {
            List<MshResolvedExpressionParameterAssociation> retVal = new List<MshResolvedExpressionParameterAssociation> ();
            List<MshExpression> expandedExpressionList = PSObjectHelper.GetDefaultPropertySet(target);

            foreach (MshExpression ex in expandedExpressionList)
            {
                retVal.Add (new MshResolvedExpressionParameterAssociation (null, ex));
            }

            return retVal;
        }

        private static List<string> GetPropertyNamesFromView(PSObject source, PSMemberViewTypes viewType)
        {
            Collection<CollectionEntry<PSMemberInfo>> memberCollection = 
                PSObject.GetMemberCollection(viewType);

            PSMemberInfoIntegratingCollection<PSMemberInfo> membersToSearch =
                new PSMemberInfoIntegratingCollection<PSMemberInfo>(source, memberCollection);

            ReadOnlyPSMemberInfoCollection<PSMemberInfo> matchedMembers = 
                membersToSearch.Match( "*", PSMemberTypes.Properties);

            List<string> retVal = new List<string>();
            foreach (PSMemberInfo member in matchedMembers)
            {
                retVal.Add(member.Name);
            }
            return retVal;
        }

        internal static List<MshResolvedExpressionParameterAssociation> ExpandAll (PSObject target)
        {
            List<string> adaptedProperties = GetPropertyNamesFromView(target, PSMemberViewTypes.Adapted);
            List<string> baseProperties = GetPropertyNamesFromView(target, PSMemberViewTypes.Base);
            List<string> extendedProperties = GetPropertyNamesFromView(target, PSMemberViewTypes.Extended);

            var displayedProperties = adaptedProperties.Count != 0 ? adaptedProperties : baseProperties;
            displayedProperties.AddRange(extendedProperties);

            Dictionary<string, object> duplicatesFinder = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            List<MshResolvedExpressionParameterAssociation> retVal = new List<MshResolvedExpressionParameterAssociation>();
            foreach (string property in displayedProperties)
            {
                if (!duplicatesFinder.ContainsKey(property))
                {
                    duplicatesFinder.Add(property, null);
                    MshExpression expr = new MshExpression(property, true);
                    retVal.Add(new MshResolvedExpressionParameterAssociation(null, expr));
                }
            }
            return retVal;
        }

        /// <summary>
        /// Helper method to handle PSComputerName and PSShowComputerName properties from
        /// the formating objects. If PSShowComputerName exists and is false, removes
        /// PSComputerName from the display.
        /// 
        /// PSShowComputerName is an internal property..so this property is always 
        /// removed from the display.
        /// </summary>
        /// <param name="so"></param>
        /// <param name="activeAssociationList"></param>
        internal static void HandleComputerNameProperties(PSObject so, List<MshResolvedExpressionParameterAssociation> activeAssociationList)
        {
            if (null != so.Properties[RemotingConstants.ShowComputerNameNoteProperty])
            {
                // always remove PSShowComputerName for the display. This is an internal property
                // that should never be visible to the user.
                Collection<MshResolvedExpressionParameterAssociation> itemsToRemove = new Collection<MshResolvedExpressionParameterAssociation>();
                foreach (MshResolvedExpressionParameterAssociation cpProp in activeAssociationList)
                {
                    if (cpProp.ResolvedExpression.ToString().Equals(RemotingConstants.ShowComputerNameNoteProperty,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        itemsToRemove.Add(cpProp);
                        break;
                    }
                }

                // remove computername for remoteobjects..only if PSShowComputerName property exists
                // otherwise the PSComputerName property does not belong to a remote object:
                // Ex: icm $s { gps } | select pscomputername --> In this case we want to show
                // PSComputerName
                if ((null != so.Properties[RemotingConstants.ComputerNameNoteProperty]) &&
                    (!PSObjectHelper.ShouldShowComputerNameProperty(so)))
                {
                    foreach (MshResolvedExpressionParameterAssociation cpProp in activeAssociationList)
                    {
                        if (cpProp.ResolvedExpression.ToString().Equals(RemotingConstants.ComputerNameNoteProperty,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            itemsToRemove.Add(cpProp);
                            break;
                        }
                    }
                }

                if (itemsToRemove.Count > 0)
                {
                    foreach (MshResolvedExpressionParameterAssociation itemToRemove in itemsToRemove)
                    {
                        activeAssociationList.Remove(itemToRemove);
                    }
                }
            }
        }

    }

}

