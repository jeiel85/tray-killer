using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Markup;

namespace TrayKiller.Core
{
    /// <summary>Korean / English strings. Language is picked at startup ("auto" follows Windows).</summary>
    public static class Loc
    {
        public static bool IsKorean { get; private set; } = true;

        public static void Init(string preference)
        {
            IsKorean = preference == "ko" || (preference != "en" && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko");
        }

        public static string T(string key)
        {
            Tuple<string, string> v;
            return S.TryGetValue(key, out v) ? (IsKorean ? v.Item1 : v.Item2) : key;
        }

        public static string F(string key, params object[] args)
        {
            try { return string.Format(T(key), args); }
            catch (FormatException) { return T(key); }
        }

        public static string Ago(DateTime t)
        {
            var d = DateTime.Now - t.ToLocalTime();
            if (d.TotalSeconds < 45) return IsKorean ? "방금" : "just now";
            if (d.TotalMinutes < 60) return IsKorean ? (int)d.TotalMinutes + "분 전" : (int)d.TotalMinutes + "m ago";
            if (d.TotalHours < 24) return IsKorean ? (int)d.TotalHours + "시간 전" : (int)d.TotalHours + "h ago";
            return IsKorean ? (int)d.TotalDays + "일 전" : (int)d.TotalDays + "d ago";
        }

        public static string Day(DateTime t)
        {
            var d = t.Date;
            if (d == DateTime.Today) return IsKorean ? "오늘" : "TODAY";
            if (d == DateTime.Today.AddDays(-1)) return IsKorean ? "어제" : "YESTERDAY";
            return IsKorean
                ? d.ToString("yyyy년 M월 d일 dddd", new CultureInfo("ko-KR"))
                : d.ToString("dddd, MMM d, yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
        }

        public static string ShortDay(DateTime t) =>
            IsKorean ? t.ToString("ddd", new CultureInfo("ko-KR")) : t.ToString("ddd", CultureInfo.InvariantCulture);

        public static string Interval(int seconds)
        {
            if (seconds % 60 == 0) return IsKorean ? seconds / 60 + "분" : seconds / 60 + " min";
            return IsKorean ? seconds + "초" : seconds + " s";
        }

        public static string Seconds(int s)
        {
            if (s >= 60) return IsKorean ? s / 60 + "분 " + s % 60 + "초" : s / 60 + "m " + s % 60 + "s";
            return IsKorean ? s + "초" : s + "s";
        }

        public static string Greeting()
        {
            int h = DateTime.Now.Hour;
            if (IsKorean)
                return h < 6 ? "늦은 밤에도 트레이를 지키고 있어요." : h < 12 ? "좋은 아침이에요. 트레이는 깨끗하게 지키고 있어요."
                    : h < 18 ? "오후에도 트레이를 깨끗하게 지키고 있어요." : "오늘 하루도 트레이를 지켰어요.";
            return h < 6 ? "Guarding your tray through the night." : h < 12 ? "Good morning. Your tray is being kept clean."
                : h < 18 ? "Keeping your tray clean this afternoon." : "Another day of a clean tray.";
        }

        static readonly Dictionary<string, Tuple<string, string>> S = new Dictionary<string, Tuple<string, string>>();

        static void A(string key, string ko, string en) => S[key] = Tuple.Create(ko, en);

        static Loc()
        {
            // navigation & chrome
            A("side.menu", "메뉴", "MENU");
            A("nav.dashboard", "대시보드", "Dashboard");
            A("nav.targets", "대상 앱", "Targets");
            A("nav.activity", "활동 기록", "Activity");
            A("nav.settings", "설정", "Settings");
            A("side.admin", "관리자 권한으로 보호 중", "Running with admin rights");
            A("side.limited", "제한 모드 (관리자 아님)", "Limited mode (not admin)");
            A("caption.minimize", "최소화", "Minimize");
            A("caption.maximize", "최대화", "Maximize");
            A("caption.close", "트레이로 숨기기", "Hide to tray");

            // status & hero
            A("status.protecting", "보호 중", "Protecting");
            A("status.paused", "일시정지됨", "Paused");
            A("hero.watching", "{0}개 앱을 트레이에서 몰아내는 중", "Keeping {0} app(s) out of your tray");
            A("hero.noRules", "감시할 앱을 추가해 주세요", "Add an app to start guarding");
            A("hero.paused", "감시를 잠시 멈췄어요", "Guarding is paused");
            A("hero.realtimeOn", "실시간 감시 켜짐", "Real-time watch on");
            A("hero.realtimeOff", "실시간 감시 꺼짐", "Real-time watch off");
            A("hero.every", "{0}마다 정기 청소", "sweep every {0}");
            A("hero.admin", "관리자 권한", "admin rights");
            A("hero.limited", "제한 모드", "limited mode");
            A("btn.pause", "일시정지", "Pause");
            A("btn.resume", "감시 재개", "Resume");
            A("btn.sweep", "지금 청소", "Sweep now");
            A("btn.addApp", "앱 추가", "Add app");
            A("btn.viewAll", "모두 보기", "View all");

            // dashboard
            A("dash.title", "대시보드", "Dashboard");
            A("dash.week", "최근 7일 처치", "Kills, last 7 days");
            A("dash.weekTotal", "합계 {0}회", "{0} total");
            A("dash.recent", "최근 활동", "Recent activity");
            A("dash.noActivity", "아직 조용하네요.", "All quiet so far.");
            A("onboard.title", "트레이에서 몰아낼 앱을 골라 보세요", "Pick the apps you want gone from the tray");
            A("onboard.body", "트레이에 떠 있는 앱이나 실행 중인 앱에서 고르면 끝이에요. 이후에는 Tray Killer가 알아서 종료하고, 자동 시작도 막아 줘요.",
                "Choose from your tray icons or running apps. Tray Killer then closes them on sight and keeps them from auto-starting.");
            A("stat.today", "오늘 처치", "Killed today");
            A("stat.today.caption", "오늘 종료한 프로세스", "processes closed today");
            A("stat.total", "누적 처치", "Total kills");
            A("stat.total.caption", "설치 후 지금까지", "since installation");
            A("stat.blocked", "차단한 시작 항목", "Startup items blocked");
            A("stat.blocked.caption", "언제든 복원할 수 있어요", "restorable any time");
            A("stat.next", "다음 청소", "Next sweep");
            A("stat.next.realtime", "실시간 감시도 작동 중", "plus real-time watching");
            A("stat.next.sweepOnly", "정기 청소만 작동 중", "periodic sweeps only");
            A("stat.next.paused", "일시정지됨", "paused");

            // targets
            A("targets.title", "대상 앱", "Target apps");
            A("targets.sub", "트레이에서 몰아낼 앱과 처리 방식을 관리해요.", "Apps to keep out of the tray, and how to deal with them.");
            A("targets.limited", "관리자 권한이 없어서 처리하지 못한 항목이 있어요. 관리자 권한으로 실행하면 관리자 앱·시스템 시작 항목·서비스까지 정리해요.",
                "Some items couldn't be handled without admin rights. Run as admin to also deal with elevated apps, machine-wide startup entries and services.");
            A("targets.empty.title", "아직 감시 중인 앱이 없어요", "No target apps yet");
            A("targets.empty.body", "‘앱 추가’를 눌러 트레이에서 거슬리는 앱을 골라 보세요.", "Click ‘Add app’ and pick whatever is cluttering your tray.");
            A("targets.blocked", "차단한 자동 시작 항목", "Blocked startup items");
            A("targets.blocked.sub", "Tray Killer가 꺼 둔 항목이에요. 복원하면 원래대로 돌아가고, 다시 차단하지 않아요.",
                "Things Tray Killer switched off. Restoring puts an item back and it won't be blocked again.");
            A("rule.anyPath", "모든 경로", "any location");
            A("rule.kills", "{0}회", "{0}×");
            A("rule.lastKill", "마지막 처치 {0}", "last kill {0}");
            A("rule.noKillYet", "아직 처치 기록 없음", "no kills yet");
            A("rule.blockedItems", "차단 항목 {0}개", "{0} blocked item(s)");
            A("rule.enabledTip", "이 앱 감시 켜기/끄기", "Guard this app on/off");
            A("rule.kill", "프로세스 종료", "Kill process");
            A("rule.kill.tip", "실행되는 즉시 종료하고 남은 트레이 아이콘을 지워요.", "Close it as soon as it runs and wipe its leftover tray icon.");
            A("rule.autostart", "자동 시작 차단", "Block auto-start");
            A("rule.autostart.tip", "레지스트리 Run 키, 시작프로그램 폴더, 작업 스케줄러에서 끕니다(백업 후).", "Removes it from Run keys, Startup folders and Task Scheduler (backed up).");
            A("rule.hideIcon", "트레이 아이콘 숨김", "Hide tray icon");
            A("rule.hideIcon.tip", "Windows 11 트레이 설정에서 아이콘을 숨김 영역으로 보내요.", "Moves its icon to the hidden overflow in Windows 11 tray settings.");
            A("rule.services", "서비스 비활성화", "Disable services");
            A("rule.services.tip", "같은 실행 파일을 쓰는 Windows 서비스를 ‘사용 안 함’으로 바꿔요. 계속 되살아나는 앱에 효과적이에요.",
                "Sets Windows services using the same executable to Disabled. Great for apps that keep coming back.");
            A("btn.runNow", "지금 처리", "Run now");
            A("btn.delete", "삭제", "Delete");
            A("btn.restore", "복원", "Restore");
            A("btn.cancel", "취소", "Cancel");
            A("btn.ok", "확인", "OK");
            A("btn.elevate", "관리자 권한으로 실행", "Run as admin");
            A("kind.registry", "레지스트리 Run", "Registry Run");
            A("kind.startupFolder", "시작프로그램 폴더", "Startup folder");
            A("kind.task", "작업 스케줄러", "Task Scheduler");
            A("kind.service", "서비스", "Service");
            A("kind.trayIcon", "트레이 아이콘", "Tray icon");
            A("where.trayIcon", "Windows 트레이 설정", "Windows tray settings");

            // activity
            A("activity.title", "활동 기록", "Activity");
            A("activity.sub", "Tray Killer가 한 일을 모두 기록해요.", "Everything Tray Killer did, as it happened.");
            A("activity.empty", "아직 기록이 없어요.", "Nothing here yet.");
            A("filter.all", "전체", "All");
            A("filter.kill", "처치", "Kills");
            A("filter.block", "차단·복원", "Blocks");
            A("filter.system", "시스템", "System");
            A("btn.clearLog", "기록 지우기", "Clear log");

            // settings
            A("settings.title", "설정", "Settings");
            A("settings.sub", "감시 방식과 시작·업데이트 동작을 정해요.", "How Tray Killer watches, starts and updates.");
            A("settings.group.protect", "감시", "PROTECTION");
            A("settings.group.startup", "시작", "STARTUP");
            A("settings.group.update", "업데이트", "UPDATES");
            A("settings.group.general", "일반", "GENERAL");
            A("settings.realtime", "실시간 감시", "Real-time watch");
            A("settings.realtime.desc", "대상 앱이 시작되는 순간 바로 종료해요. 트레이에 아이콘이 뜰 틈도 없어요.", "Kills a target the instant it starts — before it can even show an icon.");
            A("settings.interval", "정기 청소 주기", "Sweep interval");
            A("settings.interval.desc", "실시간 감시가 놓친 프로세스와 다시 생긴 자동 시작 항목을 주기적으로 정리해요.", "Periodically catches anything real-time watching missed and re-created startup entries.");
            A("int.5", "5초", "5s");
            A("int.10", "10초", "10s");
            A("int.30", "30초", "30s");
            A("int.60", "1분", "1m");
            A("int.300", "5분", "5m");
            A("settings.ghost", "유령 아이콘 정리", "Ghost icon cleanup");
            A("settings.ghost.desc", "종료한 앱의 아이콘이 트레이에 남지 않도록 대신 지워요.", "Removes the icon a killed app leaves behind in the tray.");
            A("settings.notify", "처치 알림", "Kill notifications");
            A("settings.notify.desc", "앱을 종료할 때마다 알림을 띄워요.", "Show a notification every time an app is closed.");
            A("settings.startup", "Windows 시작 시 가장 먼저 실행", "Start first with Windows");
            A("settings.startup.task", "로그온 즉시 작업 스케줄러(관리자 권한)로 시작해서 다른 트레이 앱보다 먼저 자리를 잡아요.", "Starts via an elevated Task Scheduler logon task — ahead of every other tray app.");
            A("settings.startup.run", "관리자 권한이 없어서 일반 시작프로그램으로 등록돼요. 관리자 권한으로 실행하면 가장 먼저 시작하도록 바뀌어요.", "Without admin rights this uses a regular startup entry. Run as admin to start first.");
            A("settings.admin", "관리자 권한", "Administrator rights");
            A("settings.admin.desc", "관리자 권한으로 뜨는 앱, 시스템 전체 시작 항목, 서비스까지 처리하려면 필요해요.", "Needed for elevated apps, machine-wide startup entries and services.");
            A("settings.admin.on", "켜짐", "On");
            A("settings.autoupdate", "자동 업데이트", "Automatic updates");
            A("settings.autoupdate.desc", "6시간마다 GitHub에서 새 버전을 확인하고, 창이 닫혀 있을 때 알아서 설치해요.", "Checks GitHub every 6 hours and installs new versions while the window is closed.");
            A("settings.version", "현재 버전", "Current version");
            A("btn.checkUpdate", "지금 확인", "Check now");
            A("settings.language", "언어", "Language");
            A("settings.languageNote", "다시 시작하면 적용돼요.", "Takes effect after restart.");
            A("lang.auto", "자동", "Auto");
            A("settings.about", "트레이를 깨끗하게. 오픈소스(MIT)예요.", "Keep your tray clean. Open source (MIT).");
            A("btn.dataFolder", "데이터 폴더", "Data folder");
            A("btn.uninstall", "제거", "Uninstall");

            // updates
            A("update.idle", "아직 업데이트를 확인하지 않았어요.", "Not checked yet.");
            A("update.checking", "업데이트 확인 중…", "Checking for updates…");
            A("update.upToDate", "최신 버전이에요 · {0} 확인", "You're up to date · checked {0}");
            A("update.available", "새 버전 v{0}이 나왔어요.", "Version {0} is available.");
            A("update.downloading", "v{0} 다운로드 중…", "Downloading v{0}…");
            A("update.failed", "업데이트 확인 실패: {0}", "Update check failed: {0}");
            A("dlg.update.title", "새 버전이 있어요", "Update available");
            A("dlg.update.portable", "v{0}이 나왔어요. 설치된 위치가 아닌 곳에서 실행 중이라 자동 교체는 하지 않아요. 다운로드 페이지를 열까요?",
                "Version {0} is out. This copy isn't the installed one, so it won't replace itself. Open the download page?");
            A("btn.download", "다운로드 페이지", "Open download page");
            A("btn.later", "나중에", "Later");
            A("balloon.update.title", "Tray Killer 업데이트", "Tray Killer update");
            A("balloon.updated.title", "업데이트 완료", "Updated");
            A("balloon.updated.body", "Tray Killer v{0}으로 업데이트했어요.", "Tray Killer is now v{0}.");

            // tray
            A("tray.open", "대시보드 열기", "Open dashboard");
            A("tray.add", "앱 추가…", "Add app…");
            A("tray.update", "업데이트 확인", "Check for updates");
            A("tray.updateTo", "v{0}로 업데이트", "Update to v{0}");
            A("tray.exit", "종료", "Exit");
            A("tray.status", "보호 중 · {0}개 앱 감시 · 오늘 {1}회 처치", "Protecting · {0} app(s) · {1} kill(s) today");
            A("balloon.hidden.title", "트레이에서 계속 지키고 있어요", "Still guarding from the tray");
            A("balloon.hidden.body", "창을 닫아도 Tray Killer는 계속 실행돼요. 아이콘을 누르면 다시 열려요.", "Closing the window keeps Tray Killer running. Click the icon to reopen.");
            A("balloon.killed.title", "{0} 종료함", "Closed {0}");

            // add dialog
            A("add.title", "앱 추가", "Add an app");
            A("add.sub", "트레이에서 몰아낼 앱을 골라 주세요.", "Pick the app you want gone from the tray.");
            A("add.tab.tray", "트레이 아이콘", "Tray icons");
            A("add.tab.running", "실행 중인 앱", "Running apps");
            A("add.tab.manual", "직접 입력", "Manual");
            A("add.search", "이름이나 경로로 검색", "Search by name or path");
            A("add.running", "실행 중", "Running");
            A("add.loading", "불러오는 중…", "Loading…");
            A("add.emptySource", "표시할 앱이 없어요.", "Nothing to show.");
            A("add.noMatch", "검색 결과가 없어요.", "No matches.");
            A("add.manual.name", "표시 이름", "Display name");
            A("add.manual.pattern", "실행 파일 이름 또는 경로", "Executable name or path");
            A("add.manual.hint", "예: agent.exe, C:\\Tools\\agent.exe, agent*.exe — 와일드카드(*, ?)를 쓸 수 있어요. Windows 핵심 프로세스는 보호되어 추가할 수 없어요.",
                "e.g. agent.exe, C:\\Tools\\agent.exe, agent*.exe — wildcards (*, ?) work. Core Windows processes are protected and can't be added.");
            A("add.options", "처리 방식", "WHAT TO DO");
            A("add.confirm", "추가하기", "Add");
            A("add.browseFilter", "실행 파일", "Programs");
            A("add.browseTitle", "대상 앱 실행 파일 선택", "Choose the app's executable");
            A("btn.browse", "찾아보기", "Browse");

            // dialogs
            A("dlg.delete.title", "대상 앱 삭제", "Remove target");
            A("dlg.delete.body", "‘{0}’ 감시를 그만둘까요?", "Stop guarding against ‘{0}’?");
            A("dlg.delete.bodyRestore", "‘{0}’ 감시를 그만둘까요? 이 앱의 차단 항목 {1}개도 원래대로 복원돼요.", "Stop guarding against ‘{0}’? Its {1} blocked item(s) will be restored.");
            A("dlg.restore.title", "항목 복원", "Restore item");
            A("dlg.restore.body", "‘{0}’을(를) 원래대로 되돌릴까요? 복원한 항목은 다시 차단하지 않아요.", "Put ‘{0}’ back? It won't be blocked again.");
            A("dlg.restoreFailed.title", "복원하지 못했어요", "Couldn't restore");
            A("dlg.clear.title", "기록 지우기", "Clear log");
            A("dlg.clear.body", "활동 기록을 모두 지울까요? 통계는 그대로 남아요.", "Clear the whole activity log? Statistics are kept.");
            A("dlg.uninstall.title", "Tray Killer 제거", "Uninstall Tray Killer");
            A("dlg.uninstall.body", "Tray Killer를 제거할까요? 차단했던 자동 시작 항목은 모두 원래대로 복원돼요.", "Uninstall Tray Killer? Every startup item it blocked will be restored.");
            A("dlg.uninstalled.title", "제거했어요", "Uninstalled");
            A("dlg.uninstalled.body", "차단 항목 {0}개를 복원하고 Tray Killer를 제거했어요. 그동안 고마웠어요!", "Restored {0} item(s) and removed Tray Killer. Thanks for using it!");

            // activity entries
            A("act.started", "Tray Killer 시작", "Tray Killer started");
            A("act.started.admin", "관리자 권한으로 실행 중", "running with admin rights");
            A("act.started.limited", "제한 모드로 실행 중", "running in limited mode");
            A("act.killed", "{0} 종료", "Closed {0}");
            A("act.killFailed", "{0} 종료 실패", "Couldn't close {0}");
            A("act.needAdminKill", "관리자 권한으로 실행 중인 앱이라 종료하려면 관리자 권한이 필요해요.", "The app runs elevated — Tray Killer needs admin rights to close it.");
            A("act.via.realtime", "실시간 감시", "real-time");
            A("act.via.sweep", "정기 청소", "sweep");
            A("act.trayCleaned", "트레이 아이콘 정리", "tray icon removed");
            A("act.blocked.Registry", "{0} 자동 시작 차단 (레지스트리)", "Blocked {0} auto-start (registry)");
            A("act.blocked.StartupFile", "{0} 자동 시작 차단 (시작프로그램 폴더)", "Blocked {0} auto-start (Startup folder)");
            A("act.blocked.Task", "{0} 예약 작업 비활성화", "Disabled {0} scheduled task");
            A("act.blocked.Service", "{0} 서비스 비활성화", "Disabled {0} service");
            A("act.blocked.TrayIcon", "{0} 트레이 아이콘 숨김", "Hid {0} tray icon");
            A("act.reblocked", "{0}이(가) 되살린 시작 항목을 다시 차단", "Re-blocked a startup item {0} brought back");
            A("act.problem", "처리하지 못한 항목", "Couldn't handle an item");
            A("problem.needAdmin", "관리자 권한이 필요해요: {0}", "Needs admin rights: {0}");
            A("act.ruleAdded", "{0} 감시 시작", "Now guarding against {0}");
            A("act.ruleRemoved", "{0} 감시 해제", "Stopped guarding against {0}");
            A("act.restored", "{0} 복원", "Restored {0}");
            A("act.restoreFailed", "{0} 복원 실패", "Couldn't restore {0}");
            A("act.paused", "감시 일시정지", "Guarding paused");
            A("act.resumed", "감시 재개", "Guarding resumed");
            A("act.manualSweep", "수동 청소 실행", "Manual sweep");
            A("act.runNow", "{0} 지금 처리", "Running {0} now");
            A("act.startupOn", "Windows 시작 시 실행 켜짐", "Start with Windows on");
            A("act.startupOff", "Windows 시작 시 실행 꺼짐", "Start with Windows off");
            A("act.startupFailed", "시작 설정을 바꾸지 못했어요", "Couldn't change startup setting");
            A("act.updating", "v{0} 업데이트 설치", "Installing v{0}");
            A("act.updated", "v{0}으로 업데이트 완료", "Updated to v{0}");

            // errors
            A("err.emptyPattern", "실행 파일 이름을 입력해 주세요.", "Enter an executable name.");
            A("err.protected", "Windows 핵심 프로세스({0})는 대상에 넣을 수 없어요.", "Core Windows processes ({0}) can't be targeted.");
            A("err.duplicate", "이미 감시 중인 앱이에요.", "That app is already a target.");
        }
    }

    /// <summary>XAML: Text="{l:T nav.dashboard}"</summary>
    public sealed class TExtension : MarkupExtension
    {
        public TExtension(string key) { Key = key; }
        public string Key { get; set; }
        public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
    }
}
