﻿using AzureMapsToolkit.GeoJson;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Net.Http.Headers;
using System.Globalization;
using AzureMapsToolkit.Spatial;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json;

namespace AzureMapsToolkit.Common
{
    public class BaseServices
    {
        internal string Key { get; set; }

        protected readonly JsonSerializerSettings serializerOptions;

        public BaseServices(string key)
        {
            Key = key;

            serializerOptions = new JsonSerializerSettings()
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            };
        }

        internal async Task<HttpResponseMessage> GetHttpResponseMessage(string url, string data, HttpMethod method)
        {

            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(url);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));
                using (var request = new HttpRequestMessage(method, url))
                {
                    var content = new StringContent(data); //, Encoding.UTF8, "application/json"))

                    content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    request.Content = content;
                    var response = await client.SendAsync(request); //, HttpCompletionOption.ResponseHeadersRead);

                    response.EnsureSuccessStatusCode();
                    return response;
                }
            }
        }

        string _url = string.Empty;
        internal string Url
        {
            get
            {
                return _url.Equals(string.Empty) ?
                 $"?subscription-key={Key}" : _url;
            }
            set
            {
                _url = value;
            }
        }


        internal MultiPoint GetMultipPoint(IEnumerable<Coordinate> coordinates)
        {
            double[,] points = new double[coordinates.Count(), 2];
            for (int i = 0; i < coordinates.Count(); i++)
            {
                points[i, 0] = coordinates.ElementAt(i).Longitude;
                points[i, 1] = coordinates.ElementAt(i).Latitude;
            }

            var multiPoint = new MultiPoint();
            multiPoint.Coordinates = points;

            return multiPoint;

        }


        internal IEnumerable<string> GetSearchQuery<T>(IEnumerable<T> req) where T : RequestBase
        {
            foreach (var reqItem in req)
            {
                var query = GetQuery(reqItem, false, false, '?');
                yield return query;
            }
        }

        internal string GetQuery<T>(T request, bool includeApiVersion, bool includeLanguage = true, char firstChar = '&')
            where T : RequestBase
        {
            Type type = request.GetType();

            var properties = type.GetProperties();

            var arguments = string.Empty;

            foreach (var propertyInfo in properties)
            {
                if (propertyInfo.GetValue(request) != null)
                {
                    var argumentName = string.Empty;
                    var argumentValue = string.Empty;

                    var underLayingtype = Nullable.GetUnderlyingType(propertyInfo.PropertyType);

                    if (propertyInfo?.PropertyType.IsEnum == true || underLayingtype?.IsEnum == true)
                    {
                        argumentName = Char.ToLower(propertyInfo.Name[0]) + propertyInfo.Name.Substring(1);

                        argumentValue = propertyInfo.GetValue(request).ToString().Replace(" ", "");  //.ToCamlCase();    
                    }
                    //else if (propertyInfo != null && propertyInfo.PropertyType.IsArray)
                    //{
                    //    argumentName = Char.ToLower(propertyInfo.Name[0]) + propertyInfo.Name.Substring(1)
                    //}
                    else
                    {

                        var nameAttribute = propertyInfo.GetCustomAttributes(typeof(NameArgument), false);
                        if (nameAttribute.Length > 0)
                            argumentName = ((NameArgument)nameAttribute[0]).Name;

                        else
                        {
                            var jsonAttribute = propertyInfo.GetCustomAttributes(typeof(JsonPropertyAttribute), false);
                            if (jsonAttribute.Length > 0)
                            {
                                argumentName = ((JsonPropertyAttribute)jsonAttribute[0]).PropertyName;
                            }
                        }

                        if (argumentName == string.Empty)
                            argumentName = Char.ToLower(propertyInfo.Name[0]) + propertyInfo.Name.Substring(1);


                        if (string.IsNullOrEmpty(argumentValue))
                        {
                            if (propertyInfo.PropertyType.IsArray)
                            {
                                object[] array = (object[])propertyInfo.GetValue(request);
                                argumentValue = string.Join(",", array);
                            }
                            else
                            {
                                argumentValue = propertyInfo.GetValue(request).ToString();

                                if (propertyInfo.PropertyType == typeof(int) && int.TryParse(argumentValue, out int i))
                                {
                                    argumentValue = ((int)propertyInfo.GetValue(request)).ToString(CultureInfo.InvariantCulture);
                                }

                                if (propertyInfo.PropertyType == typeof(double) || propertyInfo.PropertyType == typeof(double?)
                                    && double.TryParse(argumentValue, out double d))
                                {
                                    argumentValue = ((double)propertyInfo.GetValue(request)).ToString(CultureInfo.InvariantCulture);
                                }
                            }
                        }
                    }

                    arguments += $"{firstChar}{argumentName}={argumentValue}";
                    firstChar = '&';
                }
            }
            return arguments;
        }




        internal async Task<byte[]> GetByteArray(string baseAddress, string url)
        {
            using (var httpClient = new HttpClient())
            {
                httpClient.BaseAddress = new Uri(baseAddress);
                var content = await httpClient.GetByteArrayAsync(url);
                return content;
            }
        }

        internal async Task<Response<T>> ExecuteRequest<T, U>(string baseUrl, U req) where U : RequestBase
        {
            Url = string.Empty;
            try
            {
                if (req != null)
                    Url += GetQuery<U>(req, true);

                using (var client = GetClient(baseUrl))
                {
                    var data = await GetData<T>(client, Url);
                    var response = GetResponse<T>(data);
                    return response;
                }
            }
            catch (AzureMapsException ex)
            {
                return Response<T>.CreateErrorResponse(ex);
            }
        }

        internal string GetQuerycontent(IEnumerable<string> queryCollection)
        {
            dynamic q = new JObject();
            q.batchItems = new JArray();
            foreach (var item in queryCollection)
            {
                dynamic query = new JObject();
                query.query = item;
                q.batchItems.Add(query);
            }


            var queryContent = Newtonsoft.Json.JsonConvert.SerializeObject(q, serializerOptions);
            return queryContent;
        }

        

        internal HttpClient GetClient(string baseAddress)
        {
            var client = new HttpClient();
            if (!string.IsNullOrEmpty(baseAddress))
                client.BaseAddress = new Uri(baseAddress);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        internal Response<T> GetResponse<T>(T t)
        {
            var res = new Response<T>
            {
                Result = t
            };
            return res;
        }

        internal async Task<T> GetData<T>(HttpClient client, string url)
        {
            var res = await client.GetAsync(url);
            if (res.StatusCode == System.Net.HttpStatusCode.OK)
            {
                var json = res.Content.ReadAsStringAsync().Result;
                var val = Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json, serializerOptions);
                return val;
            }
            else
            {
                string content = res.Content.ReadAsStringAsync().Result;

                var ex = Newtonsoft.Json.JsonConvert.DeserializeObject<ErrorResponse>(content, serializerOptions);

                throw new AzureMapsException(ex);
            }

        }

        internal string GetUdidFromLocation(string location)
        {
            using (var client = GetClient(location))
            {
                var tries = 0;
                while (tries < 20)
                {
                    var result = client.GetAsync(location).Result;
                    if (result.StatusCode == System.Net.HttpStatusCode.Created)
                    {
                        var data = result.Content.ReadAsStringAsync().Result;
                        return data;
                    }
                    else if (result.StatusCode == System.Net.HttpStatusCode.Accepted)
                    {
                        System.Threading.Thread.Sleep(100);
                    }
                    else
                    {
                        throw new Exception($"Error, the document location is {location}");
                    }
                    tries += 1;
                }
            }
            throw new Exception($"Error, the document location is {location}");
        }

    }
}
