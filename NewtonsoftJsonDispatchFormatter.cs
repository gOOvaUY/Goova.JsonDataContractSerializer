﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Mime;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;
using System.Text;
using System.Xml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Formatting = Newtonsoft.Json.Formatting;

namespace Goova.JsonDataContractSerializer
{
    internal class NewtonsoftJsonDispatchFormatter : IDispatchMessageFormatter
    {
        private readonly OperationDescription operation;
        private readonly Dictionary<string, int> parameterNames;

        public NewtonsoftJsonDispatchFormatter(OperationDescription operation, bool isRequest)
        {
            this.operation = operation;
            if (isRequest)
            {
                int operationParameterCount = operation.Messages[0].Body.Parts.Count;
                if (operationParameterCount > 1)
                {
                    parameterNames = new Dictionary<string, int>();
                    for (int i = 0; i < operationParameterCount; i++)
                    {
                        parameterNames.Add(operation.Messages[0].Body.Parts[i].Name, i);
                    }
                }
            }
        }

        public void DeserializeRequest(Message message, object[] parameters)
        {
            object bodyFormatProperty;
            if (!message.Properties.TryGetValue(WebBodyFormatMessageProperty.Name, out bodyFormatProperty) || (bodyFormatProperty as WebBodyFormatMessageProperty).Format != WebContentFormat.Raw)
            {
                throw new InvalidOperationException("Incoming messages must have a body format of Raw. Is a ContentTypeMapper set on the WebHttpBinding?");
            }

            Encoding enc = Encoding.UTF8;
            if (message.Properties.ContainsKey(HttpRequestMessageProperty.Name))
            {
                HttpRequestMessageProperty prop = (HttpRequestMessageProperty) message.Properties[HttpRequestMessageProperty.Name];
                string contenttype = prop.Headers.Get("Content-Type");
                if (!string.IsNullOrEmpty(contenttype))
                {
                    ContentType tp = new ContentType(contenttype);
                    if (!string.IsNullOrEmpty(tp.CharSet))
                        enc = Encoding.GetEncoding(tp.CharSet);
                }
            }

            XmlDictionaryReader bodyReader = message.GetReaderAtBodyContents();
            bodyReader.ReadStartElement("Binary");
            byte[] rawBody = bodyReader.ReadContentAsBase64();
            MemoryStream ms = new MemoryStream(rawBody);

            StreamReader sr = new StreamReader(ms, enc);
            JsonSerializer serializer = new JsonSerializer();
            if (parameters.Length == 1)
            {
                // single parameter, assuming bare
                parameters[0] = serializer.Deserialize(sr, operation.Messages[0].Body.Parts[0].Type);
            }
            else
            {
                // multiple parameter, needs to be wrapped
                JsonReader reader = new JsonTextReader(sr);
                reader.Read();
                if (reader.TokenType != JsonToken.StartObject)
                {
                    throw new InvalidOperationException("Input needs to be wrapped in an object");
                }

                reader.Read();
                while (reader.TokenType == JsonToken.PropertyName)
                {
                    string parameterName = reader.Value as string;
                    reader.Read();
                    if (parameterNames.ContainsKey(parameterName))
                    {
                        int parameterIndex = parameterNames[parameterName];
                        parameters[parameterIndex] = serializer.Deserialize(reader, operation.Messages[0].Body.Parts[parameterIndex].Type);
                    }
                    else
                    {
                        reader.Skip();
                    }

                    reader.Read();
                }

                reader.Close();
            }

            sr.Close();
            ms.Close();
        }

        public Message SerializeReply(MessageVersion messageVersion, object[] parameters, object result)
        {
            byte[] body;
            bool isjson = true;
            if (result is Stream)
            {
                isjson = false;
                Stream r = (Stream) result;
                body = new byte[r.Length];
                r.Position = 0;
                r.Read(body, 0, (int) r.Length);
                r.Dispose();
            }
            else
            {
                string so = JsonConvert.SerializeObject(result, Formatting.None, NewtonsoftJsonClientFormatter.serSettings); //Canonicalize
                var parsedObject = JObject.Parse(so); //Canonicalize
                var normal = NewtonsoftJsonClientFormatter.SortPropertiesAlphabetically(parsedObject); //Canonicalize
                string so2 = JsonConvert.SerializeObject(normal, Formatting.None, NewtonsoftJsonClientFormatter.serSettings);
                body = Encoding.UTF8.GetBytes(so2);
            }


            Message replyMessage = Message.CreateMessage(messageVersion, operation.Messages[1].Action, new RawBodyWriter(body));
            replyMessage.Properties.Add(WebBodyFormatMessageProperty.Name, new WebBodyFormatMessageProperty(WebContentFormat.Raw));
            HttpResponseMessageProperty respProp = new HttpResponseMessageProperty();
            if (isjson)
                respProp.Headers[HttpResponseHeader.ContentType] = "application/json; charset=utf-8";
            else
            {
                bool dooctet = true;
                if (result is StreamWithHeaders)
                {
                    Dictionary<string, string> headers = ((StreamWithHeaders) result).Headers;
                    if (headers != null && headers.Count > 0)
                    {
                        dooctet = false;
                        foreach (string s in headers.Keys)
                        {
                            respProp.Headers[s] = headers[s];
                        }
                    }
                }

                if (dooctet)
                    respProp.Headers[HttpResponseHeader.ContentType] = "application/octet-stream";
            }

            replyMessage.Properties.Add(HttpResponseMessageProperty.Name, respProp);
            return replyMessage;
        }
    }
}