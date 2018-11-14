using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace KMB_ImageComparison
{
    public class ProxyAssembly : IWebProxy
    {
        public ICredentials Credentials
        {
            get
            {
                return new NetworkCredential(
                    System.ConfigurationManager.AppSettings["user"], 
                    System.Configuration.ConfigurationManager.AppSettings["password"]);
            }
            //or get { return new NetworkCredential("user", "password","domain"); }
            set { }
        }

        public Uri GetProxy(Uri destination)
        {
            return new Uri(System.Configuration.ConfigurationManager.AppSettings["address"]);
        }

        public bool IsBypassed(Uri host)
        {
            return false;
        }
    }
}
