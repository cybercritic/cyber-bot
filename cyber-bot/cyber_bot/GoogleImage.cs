using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace cyber_bot.cyber_bot
{
    public class GoogleImage
    {
        public MemoryStream GetFinalImage(string topic, out string ext)
        {
            string html = GetHtmlCode(topic);
            List<string> urls = GetUrls(html);
            var rnd = new Random((int)DateTime.Now.Ticks);

            int randomUrl = rnd.Next(0, urls.Count - 1);

            string luckyUrl = urls[randomUrl];
            ext = luckyUrl.Substring(luckyUrl.LastIndexOf('.'), luckyUrl.Length - (luckyUrl.LastIndexOf('.')));
            if (ext.Length > 4)
                ext = ext.Substring(0, 4);

            byte[] image = GetImage(luckyUrl);

            return new MemoryStream(image);
        }

        public string GetFinalImageURL(string topic)
        {
            string html = GetHtmlCode(topic);
            List<string> urls = GetUrls(html);
            var rnd = new Random((int)DateTime.Now.Ticks);

            int randomUrl = rnd.Next(0, urls.Count - 1);

            string luckyUrl = urls[randomUrl];

            return luckyUrl;
        }

        private List<string> GetUrls(string html)
        {
            var urls = new List<string>();

            int ndx = html.IndexOf("\"ou\"", StringComparison.Ordinal);

            while (ndx >= 0)
            {
                ndx = html.IndexOf("\"", ndx + 4, StringComparison.Ordinal);
                ndx++;
                int ndx2 = html.IndexOf("\"", ndx, StringComparison.Ordinal);
                string url = html.Substring(ndx, ndx2 - ndx);
                urls.Add(url);
                ndx = html.IndexOf("\"ou\"", ndx2, StringComparison.Ordinal);
            }
            return urls;
        }

        private string GetHtmlCode(string topic)
        {
            var rnd = new Random();

            string url = "https://www.google.com/search?q=" + RestSharp.Extensions.MonoHttp.HttpUtility.UrlEncode(topic) + "&tbm=isch";//&tbs=isz:m
            string data = "";

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Accept = "text/html, application/xhtml+xml, */*";
            request.UserAgent = "Mozilla/5.0 (Windows NT 6.1; WOW64; Trident/7.0; rv:11.0) like Gecko";

            var response = (HttpWebResponse)request.GetResponse();

            using (Stream dataStream = response.GetResponseStream())
            {
                if (dataStream == null)
                    return "";
                using (var sr = new StreamReader(dataStream))
                {
                    data = sr.ReadToEnd();
                }
            }
            return data;
        }

        private byte[] GetImage(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            var response = (HttpWebResponse)request.GetResponse();

            using (Stream dataStream = response.GetResponseStream())
            {
                if (dataStream == null)
                    return null;
                using (var sr = new BinaryReader(dataStream))
                {
                    byte[] bytes = sr.ReadBytes(100000000);

                    return bytes;
                }
            }

            return null;
        }
    }
}
