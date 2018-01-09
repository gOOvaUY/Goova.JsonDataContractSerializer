﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;
using System.ServiceModel.Web;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Formatting = Newtonsoft.Json.Formatting;

namespace Goova.JsonDataContractSerializer
{
    class NewtonsoftJsonClientFormatter : IClientMessageFormatter
    {
        OperationDescription operation;
        Uri operationUri;
        public NewtonsoftJsonClientFormatter(OperationDescription operation, ServiceEndpoint endpoint)
        {
            this.operation = operation;
            string endpointAddress = endpoint.Address.Uri.ToString();
            if (!endpointAddress.EndsWith("/"))
            {
                endpointAddress = endpointAddress + "/";
            }
            string uriTemplate = GetUriTemplate(operation);
            if (!string.IsNullOrEmpty("GetUriTemplate"))
            {
                this.operationUri = new Uri(endpointAddress + uriTemplate);
            }
            else
                this.operationUri = new Uri(endpointAddress + operation.Name);
        }
        private string GetUriTemplate(OperationDescription operation)
        {
            WebGetAttribute wga = operation.Behaviors.Find<WebGetAttribute>();
            if (wga != null)
            {
                return wga.UriTemplate;
            }

            WebInvokeAttribute wia = operation.Behaviors.Find<WebInvokeAttribute>();
            if (wia != null)
            {
                return wia.UriTemplate;
            }

            return null;
        }
        public object DeserializeReply(Message message, object[] parameters)
        {
            object bodyFormatProperty;
            if (!message.Properties.TryGetValue(WebBodyFormatMessageProperty.Name, out bodyFormatProperty) ||
                (bodyFormatProperty as WebBodyFormatMessageProperty).Format != WebContentFormat.Raw)
            {
                throw new InvalidOperationException("Incoming messages must have a body format of Raw. Is a ContentTypeMapper set on the WebHttpBinding?");
            }

            XmlDictionaryReader bodyReader = message.GetReaderAtBodyContents();
            Newtonsoft.Json.JsonSerializer serializer = new Newtonsoft.Json.JsonSerializer();
            bodyReader.ReadStartElement("Binary");
            byte[] body = bodyReader.ReadContentAsBase64();
            using (MemoryStream ms = new MemoryStream(body))
            {
                using (StreamReader sr = new StreamReader(ms))
                {
                    Type returnType = this.operation.Messages[1].Body.ReturnValue.Type;
                    return serializer.Deserialize(sr, returnType);
                }
            }
        }
        private readonly JsonSerializerSettings serSettings = new JsonSerializerSettings { Formatting = Formatting.None, DateFormatHandling = DateFormatHandling.IsoDateFormat, DateTimeZoneHandling = DateTimeZoneHandling.Local, NullValueHandling = NullValueHandling.Ignore };


        public Message SerializeRequest(MessageVersion messageVersion, object[] parameters)
        {
            byte[] body;
            Newtonsoft.Json.JsonSerializer serializer = new Newtonsoft.Json.JsonSerializer();
            serializer.Formatting = Formatting.None;
            serializer.DateFormatHandling = DateFormatHandling.IsoDateFormat;
            serializer.DateTimeZoneHandling = DateTimeZoneHandling.Local;
            serializer.NullValueHandling = NullValueHandling.Ignore;
            using (MemoryStream ms = new MemoryStream())
            {
                UTF8Encoding enc=new UTF8Encoding(false);
                using (StreamWriter sw = new StreamWriter(ms, enc))
                {
                    using (Newtonsoft.Json.JsonWriter writer = new Newtonsoft.Json.JsonTextWriter(sw))
                    {
                        writer.Formatting = Newtonsoft.Json.Formatting.None;
                        writer.DateFormatHandling = DateFormatHandling.IsoDateFormat;
                        writer.DateTimeZoneHandling = DateTimeZoneHandling.Local;
                        if (parameters.Length == 1)
                        {
                            string so = JsonConvert.SerializeObject(parameters[0], Formatting.None, serSettings); //Canonicalize
                            var parsedObject = JObject.Parse(so); //Canonicalize
                            var normal = SortPropertiesAlphabetically(parsedObject); //Canonicalize
                            string so2 = JsonConvert.SerializeObject(normal, Formatting.None, serSettings);
                            sw.Write(Encoding.UTF8.GetBytes(so2));
                        }
                        else
                        {
                            writer.WriteStartObject();
                            for (int i = 0; i < this.operation.Messages[0].Body.Parts.Count; i++)
                            {
                                writer.WritePropertyName(this.operation.Messages[0].Body.Parts[i].Name);
                                serializer.Serialize(writer, parameters[i]);
                            }

                            writer.WriteEndObject();
                        }

                        writer.Flush();
                        sw.Flush();
                        body = ms.ToArray();
                    }
                }
            }

            Message requestMessage = Message.CreateMessage(messageVersion, operation.Messages[0].Action, new RawBodyWriter(body));
            requestMessage.Headers.To = operationUri;
            requestMessage.Properties.Add(WebBodyFormatMessageProperty.Name, new WebBodyFormatMessageProperty(WebContentFormat.Raw));
            HttpRequestMessageProperty reqProp = new HttpRequestMessageProperty();
            reqProp.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
            requestMessage.Properties.Add(HttpRequestMessageProperty.Name, reqProp);
            return requestMessage;
        }
        private static JToken SortPropertiesAlphabetically(JToken original)
        {
            if (original is JObject)
            {
                var result = new JObject();
                foreach (var property in ((JObject)original).Properties().ToList().OrderBy(p => p.Name))
                {
                    var value = property.Value;
                    if (value != null)
                        value = SortPropertiesAlphabetically(value);
                    result.Add(property.Name, value);
                }
                return result;
            }
            if (original is JArray)
            {
                var array = original as JArray;
                for (int i = 0; i < array.Count; i++)
                    array[i] = SortPropertiesAlphabetically(array[i]);
                return array;
            }
            if (original is JValue)
            {
                JValue n = (JValue)original;
                if (n.Value is DateTime)
                {
                    DateTime dt = (DateTime)n.Value;
                    if (dt.Kind != DateTimeKind.Local)
                    {
                        dt = dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Local) : dt.ToLocalTime();
                        n.Value = dt;
                    }
                }
                return n;
            }
            return original;
        }
    }
}
