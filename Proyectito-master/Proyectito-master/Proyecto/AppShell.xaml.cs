namespace Proyecto
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Routing.RegisterRoute("Proposito", typeof(Proposito));
        }
    }
}
