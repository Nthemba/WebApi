//-----------------------------------------------------------------------------
// <copyright file="ODataInputFormatterHelper.cs" company=".NET Foundation">
//      Copyright (c) .NET Foundation and Contributors. All rights reserved. 
//      See License.txt in the project root for license information.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.OData.Common;
using Microsoft.AspNet.OData.Formatter.Deserialization;
using Microsoft.AspNet.OData.Interfaces;
using Microsoft.OData;
using Microsoft.OData.Edm;
using ODataPath = Microsoft.AspNet.OData.Routing.ODataPath;

namespace Microsoft.AspNet.OData.Formatter
{
    internal static class ODataInputFormatterHelper
    {
        internal static bool CanReadType(
            Type type,
            IEdmModel model,
            ODataPath path,
            IEnumerable<ODataPayloadKind> payloadKinds,
            Func<IEdmTypeReference, ODataDeserializer> getEdmTypeDeserializer,
            Func<Type, ODataDeserializer> getODataPayloadDeserializer)
        {
            if (type == null)
            {
                throw Error.ArgumentNull("type");
            }

            IEdmTypeReference expectedPayloadType;
            ODataDeserializer deserializer = GetDeserializer(type, path, model,
                getEdmTypeDeserializer, getODataPayloadDeserializer, out expectedPayloadType);
            if (deserializer != null)
            {
                return payloadKinds.Contains(deserializer.ODataPayloadKind);
            }

            return false;
        }

        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "The caught exception type is sent to the logger, which may throw it.")]
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "oDataMessageReader mis registered for disposal.")]
#if NETCORE
        internal static async Task<object> ReadFromStreamAsync(
#else
        internal static object ReadFromStream(
#endif
            Type type,
            object defaultValue,
            IEdmModel model,
            ODataVersion version,
            Uri baseAddress,
            IWebApiRequestMessage internalRequest,
            Func<IODataRequestMessage> getODataRequestMessage,
            Func<IEdmTypeReference, ODataDeserializer> getEdmTypeDeserializer,
            Func<Type, ODataDeserializer> getODataPayloadDeserializer,
            Func<ODataDeserializerContext> getODataDeserializerContext,
            Action<IDisposable> registerForDisposeAction,
            Action<Exception> logErrorAction)
        {
            object result;

            IEdmTypeReference expectedPayloadType;
            ODataDeserializer deserializer = GetDeserializer(type, internalRequest.Context.Path, model, getEdmTypeDeserializer, getODataPayloadDeserializer, out expectedPayloadType);
            if (deserializer == null)
            {
                throw Error.Argument("type", SRResources.FormatterReadIsNotSupportedForType, type.FullName, typeof(ODataInputFormatterHelper).FullName);
            }

            try
            {
                ODataMessageReaderSettings oDataReaderSettings = internalRequest.ReaderSettings;
                oDataReaderSettings.BaseUri = baseAddress;
                oDataReaderSettings.Validations = oDataReaderSettings.Validations & ~ValidationKinds.ThrowOnUndeclaredPropertyForNonOpenType;
                oDataReaderSettings.Version = version;

                IODataRequestMessage oDataRequestMessage = getODataRequestMessage();

                string preferHeader = RequestPreferenceHelpers.GetRequestPreferHeader(internalRequest.Headers);
                string annotationFilter = null;
                if (!String.IsNullOrEmpty(preferHeader))
                {
                    oDataRequestMessage.SetHeader(RequestPreferenceHelpers.PreferHeaderName, preferHeader);
                    annotationFilter = oDataRequestMessage.PreferHeader().AnnotationFilter;
                }

                if (annotationFilter != null)
                {
                    oDataReaderSettings.ShouldIncludeAnnotation = ODataUtils.CreateAnnotationFilter(annotationFilter);
                }

                ODataMessageReader oDataMessageReader = new ODataMessageReader(oDataRequestMessage, oDataReaderSettings, model);
                registerForDisposeAction(oDataMessageReader);

                ODataPath path = internalRequest.Context.Path;
                ODataDeserializerContext readContext = getODataDeserializerContext();
                readContext.Path = path;
                readContext.Model = model;
                readContext.ResourceType = type;
                readContext.ResourceEdmType = expectedPayloadType;

#if NETCORE
                result = await deserializer.ReadAsync(oDataMessageReader, type, readContext);
#else
                result = deserializer.Read(oDataMessageReader, type, readContext);
#endif
            }
            catch (Exception e)
            {
                logErrorAction(e);
                result = defaultValue;
            }

            return result;
        }

        private static ODataDeserializer GetDeserializer(
            Type type,
            ODataPath path,
            IEdmModel model,
            Func<IEdmTypeReference, ODataDeserializer> getEdmTypeDeserializer,
            Func<Type, ODataDeserializer> getODataPayloadDeserializer,
            out IEdmTypeReference expectedPayloadType)
        {
            expectedPayloadType = EdmLibHelpers.GetExpectedPayloadType(type, path, model);

            // Get the deserializer using the CLR type first from the deserializer provider.
            ODataDeserializer deserializer = getODataPayloadDeserializer(type);
            if (deserializer == null && expectedPayloadType != null)
            {
                // we are in typeless mode, get the deserializer using the edm type from the path.
                deserializer = getEdmTypeDeserializer(expectedPayloadType);
            }

            return deserializer;
        }
    }
}
