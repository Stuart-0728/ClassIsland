using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using FluentAvalonia.Styling;
using ClassIsland.Views;
using ClassIsland.Models;
using System.Text.Json;

AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions {UseHeadlessDrawing=false}).UseSkia().SetupWithoutStarting();
var directory=Path.Combine(Path.GetTempPath(),"BashuHomeworkWindowSmoke-"+Guid.NewGuid());
Directory.CreateDirectory(directory);
var statePath=Path.Combine(directory,"Window.json");
BashuHomeworkPreviewWindow? window=null;
void Check(bool ok,string label) { if(!ok) throw new Exception(label);Console.WriteLine("PASS "+label); }
void Layout() { Dispatcher.UIThread.RunJobs();window!.UpdateLayout(); }
void Click(Button button) { var p=button.TranslatePoint(new Point(button.Bounds.Width/2,button.Bounds.Height/2),window!)!.Value;window!.MouseDown(p,MouseButton.Left);window.MouseUp(p,MouseButton.Left);Layout(); }
void Capture(string name) {using var warmup=window!.CaptureRenderedFrame();Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);Dispatcher.UIThread.RunJobs();using var frame=window!.CaptureRenderedFrame();Check(frame!=null,"actual window renders "+name);var output=Environment.GetEnvironmentVariable("HOMEWORK_WINDOW_QA_OUTPUT");if(output!=null){Directory.CreateDirectory(output);frame!.Save(Path.Combine(output,name+".png"));}}
try
{
    window=new BashuHomeworkPreviewWindow(statePath);window.Show();Layout();
    Check(window.Width==520 && window.Height==62,"compact window opens with wider readable viewport");
    Check(window.FindControl<Canvas>("CompactViewport")!.Bounds.Width>300,"compact text retains usable reading width");
    Capture("compact-default");
    var right=window.Position.X+window.Width*window.RenderScaling;var top=window.Position.Y;
    foreach(var scale in new[]{0.8,1.0,1.4}) {window.ViewModel.WindowScale=scale;Layout();Check(window.Width==520,"rendered compact width independent at scale "+scale);Check(Math.Abs(window.FindControl<Canvas>("CompactViewport")!.TransformToVisual(window)!.Value.M11-scale)<0.001,"rendered content uses latest scale");Check(Math.Abs(window.Position.X+window.Width*window.RenderScaling-right)<=1 && window.Position.Y==top,"scaling preserves chosen position");}
    Capture("compact-large-text");
    var settingsButton=window.FindControl<Button>("CompactSettingsButton")!;
    Click(settingsButton);
    Check(settingsButton.Flyout!.IsOpen,"compact settings button opens without dragging the window");
    var sliders=((Border)((Flyout)settingsButton.Flyout).Content!).GetVisualDescendants().OfType<Slider>().ToArray();
    Check(sliders.Length==2,"settings expose independent content scale and capsule width");
    sliders[1].Value=720;Layout();
    Check(window.Width==720 && window.ViewModel.CompactWidth==720,"visible width slider updates the actual capsule");
    settingsButton.Flyout.Hide();
    window.ViewModel.CompactWidth=720;window.ViewModel.WindowScale=1;Layout();
    Check(window.Width==720,"width control independently widens rendered capsule");
    Click(window.FindControl<Button>("ExpandButton")!);Check(window.ViewModel.Expanded,"expand button remains clickable without starting drag");
    Capture("expanded");Click(window.FindControl<Button>("FoldButton")!);Check(!window.ViewModel.Expanded && window.Width==720,"fold button restores chosen compact width");
    var start=window.Position;
    window.MouseDown(new Point(60,20),MouseButton.Left);window.MouseMove(new Point(-40,100));window.MouseUp(new Point(60,20),MouseButton.Left);Layout();
    Check(window.Position!=start,"mouse can drag the compact header");
    foreach(var type in new[]{PointerType.Touch,PointerType.Pen})
    {
        start=window.Position;
        var pointer=new Pointer(type==PointerType.Touch?42:43,type,true);var properties=new PointerPointProperties();var header=window.FindControl<Border>("CompactHeader")!;
        header.RaiseEvent(new PointerPressedEventArgs(header,pointer,window,new Point(60,20),1,properties,KeyModifiers.None,1));
        window.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent,window,pointer,window,new Point(100,60),2,properties,KeyModifiers.None));
        window.RaiseEvent(new PointerReleasedEventArgs(window,pointer,window,new Point(60,20),3,properties,KeyModifiers.None,MouseButton.Left));Layout();
        Check(window.Position!=start,type+" dragging works without a mouse-left-button flag");
    }
    var lostPointer=new Pointer(44,PointerType.Touch,true);
    var dragHeader=window.FindControl<Border>("CompactHeader")!;
    dragHeader.RaiseEvent(new PointerPressedEventArgs(dragHeader,lostPointer,window,new Point(60,20),4,new PointerPointProperties(),KeyModifiers.None,1));
    window.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent,window,lostPointer,window,new Point(70,30),5,new PointerPointProperties(),KeyModifiers.None));
    lostPointer.Capture(null);Layout();
    Check(JsonSerializer.Deserialize<BashuHomeworkWindowState>(File.ReadAllText(statePath))!.HasPosition,"interrupted touch capture persists placement safely");
    window.ViewModel.WindowScale=1.2;window.ViewModel.CompactWidth=740;Layout();
    var savedPosition=window.Position;window.Close();window=null;
    var saved=JsonSerializer.Deserialize<BashuHomeworkWindowState>(File.ReadAllText(statePath))!;
    Check(saved.HasPosition && saved.WindowScale==1.2 && saved.CompactWidth==740,"dragged placement and display preferences are written to disk");
    window=new BashuHomeworkPreviewWindow(statePath);window.Show();Layout();
    Check(window.Position==savedPosition && window.Width==740 && window.ViewModel.WindowScale==1.2,"reopened actual window restores last position, width and scale");
    Capture("compact-restored");
    window.Close();window=null;
    saved.ScreenName="removed-monitor";saved.ScreenX=100000;saved.ScreenY=100000;saved.RightInset=100000;saved.TopInset=100000;
    File.WriteAllText(statePath,JsonSerializer.Serialize(saved));
    window=new BashuHomeworkPreviewWindow(statePath);window.Show();Layout();var area=window.Screens.ScreenFromWindow(window)!.WorkingArea;
    Check(window.Position.X>=area.X && window.Position.Y>=area.Y && window.Position.X+window.Width*window.RenderScaling<=area.Right+1 && window.Position.Y+window.Height*window.RenderScaling<=area.Bottom+1,"removed monitor restores a fully visible window");
    var closePointer=new Pointer(45,PointerType.Touch,true);var closeHeader=window.FindControl<Border>("CompactHeader")!;
    closeHeader.RaiseEvent(new PointerPressedEventArgs(closeHeader,closePointer,window,new Point(60,20),6,new PointerPointProperties(),KeyModifiers.None,1));
    window.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent,window,closePointer,window,new Point(70,10),7,new PointerPointProperties(),KeyModifiers.None));
    window.Close();window=null;
    Check(JsonSerializer.Deserialize<BashuHomeworkWindowState>(File.ReadAllText(statePath))!.HasPosition,"closing during touch drag saves without accessing a disposed window");
    Console.WriteLine("All real homework window checks passed.");
}
finally {window?.Close();Directory.Delete(directory,true);}
public class TestApp:Application {public override void Initialize(){RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Light;Styles.Add(new FluentAvaloniaTheme());}}
