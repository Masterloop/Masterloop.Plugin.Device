﻿using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Masterloop.Plugin.Device
{
    public class ExtendedWebClient
    {
        public ExtendedWebClient()
        {
            StatusCode = HttpStatusCode.Unused;
            StatusDescription = string.Empty;
            Timeout = 30;
        }

        public string Username { get; set; }

        public string Password { get; set; }

        public string Accept { get; set; }

        public string ContentType { get; set; }

        public HttpStatusCode StatusCode { get; set; }

        public string StatusDescription { get; set; }

        /// <summary>
        /// Time in seconds.
        /// </summary>
        public int Timeout { get; set; }

        public string DownloadString(string url)
        {
            HttpWebRequest request = CreateRequestObject(url, "GET");
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                this.StatusCode = response.StatusCode;
                this.StatusDescription = response.StatusDescription;
                string data = reader.ReadToEnd();
                return data;
            }
        }

        public async Task<string> DownloadStringAsync(string url)
        {
            HttpWebRequest request = CreateRequestObject(url, "GET");
            HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync();
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                this.StatusCode = response.StatusCode;
                this.StatusDescription = response.StatusDescription;
                string data = await reader.ReadToEndAsync();
                return data;
            }
        }

        public byte[] DownloadBytes(string url)
        {
            HttpWebRequest request = CreateRequestObject(url, "GET");
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            using (BinaryReader reader = new BinaryReader(response.GetResponseStream()))
            {
                this.StatusCode = response.StatusCode;
                this.StatusDescription = response.StatusDescription;
                return ReadAllBytes(reader);
            }
        }

        public async Task<byte[]> DownloadBytesAsync(string url)
        {
            HttpWebRequest request = CreateRequestObject(url, "GET");
            HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync();
            using (BinaryReader reader = new BinaryReader(response.GetResponseStream()))
            {
                this.StatusCode = response.StatusCode;
                this.StatusDescription = response.StatusDescription;
                return ReadAllBytes(reader);
            }
        }

        public string UploadString(string url, string body)
        {
            HttpWebRequest request = CreateRequestObject(url, "POST");
            request.ContentType = this.ContentType;
            using (Stream writer = (Stream)request.GetRequestStream())
            {
                byte[] data = Encoding.UTF8.GetBytes(body);
                writer.Write(data, 0, data.Length);
            }
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                this.StatusCode = response.StatusCode;
                this.StatusDescription = response.StatusDescription;
                string data = reader.ReadToEnd();
                return data;
            }
        }

        public async Task<string> UploadStringAsync(string url, string body)
        {
            HttpWebRequest request = CreateRequestObject(url, "POST");
            request.ContentType = this.ContentType;
            using (Stream writer = (Stream)await request.GetRequestStreamAsync())
            {
                byte[] data = Encoding.UTF8.GetBytes(body);
                await writer.WriteAsync(data, 0, data.Length);
            }
            HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync();
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                this.StatusCode = response.StatusCode;
                this.StatusDescription = response.StatusDescription;
                string data = await reader.ReadToEndAsync();
                return data;
            }
        }

        public void Delete(string url)
        {
            HttpWebRequest request = CreateRequestObject(url, "DELETE");
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            this.StatusCode = response.StatusCode;
            this.StatusDescription = response.StatusDescription;
        }

        public async Task DeleteAsync(string url)
        {
            HttpWebRequest request = CreateRequestObject(url, "DELETE");
            HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync();
            this.StatusCode = response.StatusCode;
            this.StatusDescription = response.StatusDescription;
        }

        private HttpWebRequest CreateRequestObject(string url, string method)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            if (this.Username != null && this.Password != null)
            {
                request.Credentials = new NetworkCredential(this.Username, this.Password);
                string authInfo = this.Username + ":" + this.Password;
                authInfo = Convert.ToBase64String(Encoding.Default.GetBytes(authInfo));
                request.Headers["Authorization"] = "Basic " + authInfo;
            }
            request.Accept = this.Accept;
            request.Method = method;
            request.Timeout = Timeout * 1000;
            request.ReadWriteTimeout = Timeout * 1000;
            return request;
        }

        private byte[] ReadAllBytes(BinaryReader reader)
        {
            const int bufferSize = 4096;
            using (var ms = new MemoryStream())
            {
                byte[] buffer = new byte[bufferSize];
                int count;
                while ((count = reader.Read(buffer, 0, buffer.Length)) != 0)
                    ms.Write(buffer, 0, count);
                return ms.ToArray();
            }
        }
    }
}
