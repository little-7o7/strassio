using System;
using System.Windows;
using System.Windows.Controls;
using Corel.Interop.VGCore;
using CorelApplication = Corel.Interop.VGCore.Application;

namespace Strassio.Corel
{
    /// <summary>
    /// Докер Strassio. CorelDRAW создаёт этот UserControl сам (см. AppUI.xslt, itemData type="wpfhost")
    /// и передаёт в конструктор свой объект Application — никакой COM-регистрации не нужно.
    /// </summary>
    public partial class Docker : UserControl
    {
        private readonly CorelApplication app;

        public Docker(object app)
        {
            InitializeComponent();
            this.app = app as CorelApplication;
        }

        // Нужен WPF-дизайнеру и на случай ошибки приведения app в конструкторе выше.
        public Docker()
        {
            InitializeComponent();
        }

        private void CreateTestStone_Click(object sender, RoutedEventArgs e)
        {
            if (app == null)
            {
                StatusText.Text = "Нет связи с CorelDRAW (app == null).";
                return;
            }

            Document doc = app.ActiveDocument;
            if (doc == null)
            {
                StatusText.Text = "Нет открытого документа. Создайте новый документ и попробуйте снова.";
                return;
            }

            bool prevOptimization = app.Optimization;
            bool prevEventsEnabled = app.EventsEnabled;
            cdrUnit prevUnit = doc.Unit;

            try
            {
                app.Optimization = true;
                app.EventsEnabled = false;
                doc.Unit = cdrUnit.cdrMillimeter;

                doc.BeginCommandGroup("Strassio: тестовая страза ss6");
                try
                {
                    Layer layer = doc.ActiveLayer;
                    const double diameterMm = 2.4; // ss6, см. docs/SPEC.md, раздел 3.1
                    double radius = diameterMm / 2.0;

                    Shape stone = layer.CreateEllipse2(0, 0, radius, radius);
                    stone.Name = "ss6 Тест";
                    stone.Fill.UniformColor = app.CreateRGBColor(229, 57, 53); // #E53935, см. SPEC 3.2
                    stone.Outline.SetNoOutline();
                }
                finally
                {
                    doc.EndCommandGroup();
                }

                StatusText.Text = "Готово: страза ss6 создана в точке (0, 0) мм. Ctrl+Z — отменить.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка: " + ex.Message;
            }
            finally
            {
                doc.Unit = prevUnit;
                app.EventsEnabled = prevEventsEnabled;
                app.Optimization = prevOptimization;
                app.Refresh();
            }
        }
    }
}
