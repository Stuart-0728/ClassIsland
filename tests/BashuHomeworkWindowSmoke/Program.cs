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
    Check(!window.ShowInTaskbar,"homework capsule does not create a taskbar entry");
    Check(window.Topmost && window.ViewModel.PinAngle==0,"pinned capsule uses upright pin by default");
    Click(window.FindControl<Button>("CompactPinButton")!);
    Check(!window.Topmost && window.ViewModel.PinAngle==-40,"compact pin tilts when topmost is disabled without dragging");
    Check(JsonSerializer.Deserialize<BashuHomeworkWindowState>("{}")!.IsPinned,"existing preferences retain default topmost behavior");
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
    var initialPosition=window.Position;var initialHeight=window.Height;var initialScale=window.ViewModel.WindowScale;
    var popupContent=(Border)((Flyout)settingsButton.Flyout).Content!;
    var popupPosition=popupContent.PointToScreen(new Point());
    var unchangedFile=File.ReadAllText(statePath);
    foreach(var width in new[]{600,800,680,720})
    {
        sliders[1].Value=width;sliders[0].Value=1.1;Layout();
        Check(window.Width==520 && window.Height==initialHeight && window.Position==initialPosition && window.ViewModel.WindowScale==initialScale && popupContent.PointToScreen(new Point())==popupPosition,"sliders remain stationary while editing width "+width);
    }
    Check(File.ReadAllText(statePath)==unchangedFile,"slider movement does not repeatedly write preferences");
    Check(window.ViewModel.DraftCompactWidthText=="720" && window.ViewModel.DraftWindowScaleText=="110%","draft labels show selected width and scale before applying");
    settingsButton.Flyout.Hide();Layout();
    Check(window.Width==720 && window.ViewModel.WindowScale==1.1 && window.ViewModel.CompactWidth==720,"closing settings applies width and scale together");
    Check(JsonSerializer.Deserialize<BashuHomeworkWindowState>(File.ReadAllText(statePath))!.CompactWidth==720,"closing settings persists final width");
    window.ViewModel.CompactWidth=720;window.ViewModel.WindowScale=1;Layout();
    Check(window.Width==720,"width control independently widens rendered capsule");
    Click(window.FindControl<Button>("ExpandButton")!);Check(window.ViewModel.Expanded,"expand button remains clickable without starting drag");
    Check(!window.Topmost && !window.ShowInTaskbar,"expanded window preserves unpinned state and stays off taskbar");
    Click(window.FindControl<Button>("ExpandedPinButton")!);Check(window.Topmost && window.ViewModel.PinAngle==0,"expanded pin restores topmost and upright orientation");
    var expandedSettings=window.FindControl<Button>("ExpandedSettingsButton")!;Click(expandedSettings);
    var expandedSliders=((Border)((Flyout)expandedSettings.Flyout!).Content!).GetVisualDescendants().OfType<Slider>().ToArray();
    var expandedWidth=window.Width;var expandedPosition=window.Position;
    expandedSliders[0].Value=1.2;expandedSliders[1].Value=760;Layout();
    Check(window.Width==expandedWidth && window.Position==expandedPosition,"expanded settings also defer resizing");
    expandedSettings.Flyout.Hide();Layout();
    Check(window.ViewModel.WindowScale==1.2 && window.ViewModel.CompactWidth==760,"expanded settings commit selected preferences");
    window.ViewModel.WindowScale=1;window.ViewModel.CompactWidth=720;Layout();
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
    Click(window.FindControl<Button>("CompactPinButton")!);
    var savedPosition=window.Position;window.Close();window=null;
    var saved=JsonSerializer.Deserialize<BashuHomeworkWindowState>(File.ReadAllText(statePath))!;
    Check(saved.HasPosition && saved.WindowScale==1.2 && saved.CompactWidth==740,"dragged placement and display preferences are written to disk");
    window=new BashuHomeworkPreviewWindow(statePath);window.Show();Layout();
    Check(window.Position==savedPosition && window.Width==740 && window.ViewModel.WindowScale==1.2,"reopened actual window restores last position, width and scale");
    Check(!window.Topmost && !window.ViewModel.IsPinned && window.ViewModel.PinAngle==-40 && !window.ShowInTaskbar,"reopening restores unpinned state without a taskbar entry");
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
    window=new BashuHomeworkPreviewWindow(statePath);window.Show();Layout();
    var closingSettings=window.FindControl<Button>("CompactSettingsButton")!;Click(closingSettings);
    window.ViewModel.DraftCompactWidth=810;window.Close();window=null;
    Check(JsonSerializer.Deserialize<BashuHomeworkWindowState>(File.ReadAllText(statePath))!.CompactWidth==810,"closing window with settings open commits draft safely");
    Console.WriteLine("All real homework window checks passed.");
}
finally {window?.Close();Directory.Delete(directory,true);}
public class TestApp:Application {public override void Initialize(){RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Light;Styles.Add(new FluentAvaloniaTheme());}}
