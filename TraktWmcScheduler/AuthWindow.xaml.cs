using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using TraktSharp;

namespace TraktWmcScheduler
{
    /// <summary>
    /// Interaction logic for AuthWindow.xaml
    /// </summary>
    public partial class AuthWindow : Window
    {
        public TraktClient Client { get; set; }

        public AuthWindow()
        {
            InitializeComponent();
        }

        public AuthWindow(TraktClient client) : this()
        {
            this.Client = client;
            Load();
        }

        private void Load()
        {
            AuthorizeBrowser.Navigate(Client.Authentication.OAuthAuthorizationUri);
        }

        private void AuthorizeBrowserNavigating(object sender, System.Windows.Navigation.NavigatingCancelEventArgs e)
        {
            if (!e.Uri.AbsoluteUri.StartsWith(Client.Authentication.OAuthRedirectUri, StringComparison.CurrentCultureIgnoreCase))
            {
                return;
            }
            Client.Authentication.ParseOAuthAuthorizationResponse(e.Uri);
            e.Cancel = true;
            this.DialogResult = true;
            this.Close();
        }
    }
}
