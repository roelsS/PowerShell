/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Internal;
using System.Globalization;

namespace Microsoft.PowerShell.Commands.Internal.Format
{
    /// <summary>
    /// internal class to manage the grouping algorithm for the 
    /// format-xxx commands
    /// </summary>
    internal sealed class GroupingInfoManager
    {
        /// <summary>
        /// Initalize with the grouping property data
        /// </summary>
        /// <param name="groupingExpression">name of the grouping porperty</param>
        /// <param name="displayLabel">dispaly name of the property</param>
        internal void Initialize (MshExpression groupingExpression, string displayLabel)
        {
            groupingKeyExpression = groupingExpression;
            label = displayLabel;
        }

        internal object CurrentGroupingKeyPropertyValue
        {
            get { return this.currentGroupingKeyPropertyValue; }
        }

        internal string GroupingKeyDisplayName
        {
            get 
            {
                if (this.label != null)
                    return this.label;
                return this.groupingKeyDisplayName;
            }
        }

        /// <summary>
        /// compute the string value of the grouping property
        /// </summary>
        /// <param name="so">object to use to compute the property value</param>
        /// <returns>true if there was an update</returns>
        internal bool UpdateGroupingKeyValue (PSObject so)
        {
            if (this.groupingKeyExpression == null)
                return false;

            List<MshExpressionResult> results = this.groupingKeyExpression.GetValues (so);

            // if we have more that one match, we have to select the first one
            if (results.Count > 0 && results[0].Exception == null)
            {
                // no exception got thrown, so we can update
                object newValue = results[0].Result;
                object oldValue = this.currentGroupingKeyPropertyValue;

                this.currentGroupingKeyPropertyValue = newValue;

                // now do the comparison
                bool update = !(IsEqual(this.currentGroupingKeyPropertyValue, oldValue) ||
                                IsEqual(oldValue, this.currentGroupingKeyPropertyValue));

                if (update && this.label == null)
                {
                    this.groupingKeyDisplayName = results[0].ResolvedExpression.ToString();
                }
                return update;
            }

            // we had no matches or we could not get the value:
            // NOTICE: we need to do this to avoid starting a new group every time 
            // there is a failure to read the grouping property.
            // For example, for AD, there are objects that throw when trying
            // to read the "distinguishedName" property (used by the brokered property "ParentPath)
            return false;
        }

        private static bool IsEqual(object first, object second)
        {
            try
            {
                return LanguagePrimitives.Compare(first, second, true, CultureInfo.CurrentCulture) == 0;
            }
            catch (InvalidCastException)
            {
            }
            catch (ArgumentException)
            {
                // Note that this will occur if the objects do not support
                // IComparable.  We fall back to comparing as strings.
            }

            // being here means the first object doesn't support ICompare
            // or an Exception was raised win Compare
            string firstString = PSObject.AsPSObject(first).ToString();
            string secondString = PSObject.AsPSObject(second).ToString();

            return string.Compare(firstString, secondString, StringComparison.CurrentCultureIgnoreCase) == 0;
        }

        /// <summary>
        /// value of the display label passed in. 
        /// </summary>
        private string label = null;

        /// <summary>
        /// value of the current active grouping key
        /// </summary>
        private string groupingKeyDisplayName = null;

        /// <summary>
        /// name of the current grouping key
        /// </summary>
        private MshExpression groupingKeyExpression = null;

        /// <summary>
        /// the current value of the grouping key
        /// </summary>
        private object currentGroupingKeyPropertyValue = AutomationNull.Value;
    }

}

