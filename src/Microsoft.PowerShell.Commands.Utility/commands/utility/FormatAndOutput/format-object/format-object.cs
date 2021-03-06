/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/
using System;
using System.Management.Automation;
using Microsoft.PowerShell.Commands.Internal.Format;

namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// implementation for the format-custom command. It just calls the formatting
    /// engine on complex shape
    /// </summary>
    [Cmdlet("Format", "Custom", HelpUri = "http://go.microsoft.com/fwlink/?LinkID=113301")]
    public class FormatCustomCommand : OuterFormatShapeCommandBase
    {
        /// <summary>
        /// constructor to se the inner command
        /// </summary>
        public FormatCustomCommand()
        {
            this.implementation = new InnerFormatShapeCommand (FormatShape.Complex);
        }


        #region Command Line Switches

        /// <summary>
        /// Positional parameter for properties, property sets and table sets
        /// specified on the command line.
        /// The paramater is optional, since the defaults
        /// will be determined using property sets, etc.
        /// </summary>
        [Parameter (Position = 0)]
        public object[] Property
        {
            get { return props; }
            set { props = value; }
        }

        private object[] props;

        /// <summary>
        /// 
        /// </summary>
        /// <value></value>
        [ValidateRangeAttribute (1, int.MaxValue)]
        [Parameter]
        public int Depth
        {
            get { return depth; }
            set { depth = value; }
        }

        private int depth = ComplexSpecificParameters.maxDepthAllowable;

        #endregion

        internal override FormattingCommandLineParameters GetCommandLineParameters ()
        {
            FormattingCommandLineParameters parameters = new FormattingCommandLineParameters ();

            if (this.props != null)
            {
                ParameterProcessor processor = new ParameterProcessor (new FormatObjectParameterDefinition());
                TerminatingErrorContext invocationContext = new TerminatingErrorContext (this);
                parameters.mshParameterList = processor.ProcessParameters (this.props, invocationContext);
            }

            if (!string.IsNullOrEmpty (this.View))
            {
                // we have a view command line switch
                if (parameters.mshParameterList.Count != 0)
                {
                    ReportCannotSpecifyViewAndProperty ();
                }
                parameters.viewName = this.View;
            }

            parameters.groupByParameter = this.ProcessGroupByParameter ();
            parameters.forceFormattingAlsoOnOutOfBand = this.Force;
            if (this.showErrorsAsMessages.HasValue)
                parameters.showErrorsAsMessages = this.showErrorsAsMessages;
            if (this.showErrorsInFormattedOutput.HasValue)
                parameters.showErrorsInFormattedOutput = this.showErrorsInFormattedOutput;

            parameters.expansion = ProcessExpandParameter ();

            ComplexSpecificParameters csp = new ComplexSpecificParameters ();
            csp.maxDepth = this.depth;
            parameters.shapeParameters = csp;

            return parameters;
        }
    }
}

