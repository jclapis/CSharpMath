namespace CSharpMath.Forms {
  using Rendering;
  using Rendering.Text;
  using SkiaSharp;
  using Xamarin.Forms;
  using Xamarin.Forms.Xaml;
  [ContentProperty(nameof(LaTeX)), XamlCompilation(XamlCompilationOptions.Compile)]
  public class TextView : BaseView<TextPainter, TextSource> {
    protected override string LaTeXFromSource(TextSource source) => source.LaTeX;
    protected override TextSource SourceFromLaTeX(string latex) => TextSource.FromLaTeX(latex);
    public TextAtom Atom { get => Source.Atom; set => Source = new TextSource(value); }
    public float LineWidth {
      get => (float)GetValue(LineWidthProperty);
      set => SetValue(LineWidthProperty, value);
    }
    public static readonly BindableProperty LineWidthProperty =
      BindableProperty.Create(nameof(LineWidth), typeof(float), typeof(TextView));
  }
}