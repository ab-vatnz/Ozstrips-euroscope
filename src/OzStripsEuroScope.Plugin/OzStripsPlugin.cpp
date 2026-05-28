#include "OzStripsPlugin.h"

#include <algorithm>
#include <cctype>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <set>
#include <sstream>
#include <vector>

using namespace EuroScopePlugIn;

namespace
{
constexpr const char* kPluginName = "OzStrips EuroScope";
constexpr const char* kPluginVersion = "0.1.0";
constexpr const char* kAuthor = "Adam / Max Rumsey";
constexpr const char* kCopyright = "Used with OzStrips owner permission";

OzStripsEuroScopePlugin* g_plugin = nullptr;

std::string Safe(const char* value)
{
    return value == nullptr ? std::string() : std::string(value);
}

std::string Trim(std::string value)
{
    while (!value.empty() && std::isspace(static_cast<unsigned char>(value.front())) != 0)
    {
        value.erase(value.begin());
    }

    while (!value.empty() && std::isspace(static_cast<unsigned char>(value.back())) != 0)
    {
        value.pop_back();
    }

    return value;
}

std::string ToLower(std::string value)
{
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char c) {
        return static_cast<char>(std::tolower(c));
    });
    return value;
}

std::string ToUpper(std::string value)
{
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char c) {
        return static_cast<char>(std::toupper(c));
    });
    return value;
}

bool StartsWithIgnoreCase(const std::string& value, const std::string& prefix)
{
    return value.size() >= prefix.size() &&
        ToUpper(value.substr(0, prefix.size())) == ToUpper(prefix);
}

std::string FileNameOnly(const std::string& path)
{
    const auto slash = path.find_last_of("\\/");
    return slash == std::string::npos ? path : path.substr(slash + 1);
}

std::string ParentDirectory(const std::string& path)
{
    const auto slash = path.find_last_of("\\/");
    return slash == std::string::npos ? std::string() : path.substr(0, slash);
}

bool FileExists(const std::string& path)
{
    std::ifstream file(path);
    return file.good();
}

std::string WithEseExtension(const std::string& path)
{
    const auto slash = path.find_last_of("\\/");
    const auto dot = path.find_last_of('.');
    if (dot != std::string::npos && (slash == std::string::npos || dot > slash))
    {
        return path.substr(0, dot) + ".ese";
    }

    return path + ".ese";
}

bool TrySectorPath(const std::string& path, std::string& resolved)
{
    if (FileExists(path))
    {
        resolved = path;
        return true;
    }

    const auto esePath = WithEseExtension(path);
    if (FileExists(esePath))
    {
        resolved = esePath;
        return true;
    }

    return false;
}

std::string GetEnvironmentString(const char* name)
{
    char value[MAX_PATH] = {};
    const auto size = GetEnvironmentVariableA(name, value, MAX_PATH);
    return size == 0 || size >= MAX_PATH ? std::string() : std::string(value);
}

std::string JoinPath(const std::string& left, const std::string& right)
{
    if (left.empty())
    {
        return right;
    }

    const auto last = left[left.size() - 1];
    return (last == '\\' || last == '/') ? left + right : left + "\\" + right;
}

std::string FindFileRecursive(const std::string& root, const std::vector<std::string>& fileNames, int depth)
{
    if (root.empty() || depth < 0)
    {
        return std::string();
    }

    for (const auto& name : fileNames)
    {
        const auto candidate = JoinPath(root, name);
        if (FileExists(candidate))
        {
            return candidate;
        }
    }

    WIN32_FIND_DATAA data = {};
    const auto pattern = JoinPath(root, "*");
    HANDLE find = FindFirstFileA(pattern.c_str(), &data);
    if (find == INVALID_HANDLE_VALUE)
    {
        return std::string();
    }

    do
    {
        if ((data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0)
        {
            continue;
        }

        const auto name = std::string(data.cFileName);
        if (name == "." || name == "..")
        {
            continue;
        }

        const auto found = FindFileRecursive(JoinPath(root, name), fileNames, depth - 1);
        if (!found.empty())
        {
            FindClose(find);
            return found;
        }
    }
    while (FindNextFileA(find, &data));

    FindClose(find);
    return std::string();
}

std::vector<std::string> SplitSidLine(const std::string& line)
{
    std::vector<std::string> parts;
    std::string part;
    std::istringstream stream(line);

    while (std::getline(stream, part, ':') && parts.size() < 5)
    {
        parts.push_back(Trim(part));
    }

    return parts;
}

bool AirportMatchesSidElement(const std::string& airport, const std::string& origin)
{
    const auto airportUpper = ToUpper(Trim(airport));
    const auto originUpper = ToUpper(Trim(origin));

    if (airportUpper.empty() || originUpper.empty())
    {
        return false;
    }

    if (airportUpper == originUpper)
    {
        return true;
    }

    return airportUpper.size() == 2 &&
        originUpper.size() == 4 &&
        originUpper.substr(2) == airportUpper;
}

std::string JsonEscape(const std::string& value)
{
    std::ostringstream out;

    for (const char ch : value)
    {
        switch (ch)
        {
        case '\\':
            out << "\\\\";
            break;
        case '"':
            out << "\\\"";
            break;
        case '\b':
            out << "\\b";
            break;
        case '\f':
            out << "\\f";
            break;
        case '\n':
            out << "\\n";
            break;
        case '\r':
            out << "\\r";
            break;
        case '\t':
            out << "\\t";
            break;
        default:
            if (static_cast<unsigned char>(ch) < 0x20)
            {
                char encoded[7] = {};
                std::snprintf(encoded, sizeof(encoded), "\\u%04x", ch);
                out << encoded;
            }
            else
            {
                out << ch;
            }
        }
    }

    return out.str();
}

void JsonString(std::ostringstream& out, const char* name, const std::string& value, bool comma = true)
{
    out << "\"" << name << "\":\"" << JsonEscape(value) << "\"";
    if (comma)
    {
        out << ",";
    }
}

void JsonInt(std::ostringstream& out, const char* name, int value, bool comma = true)
{
    out << "\"" << name << "\":" << value;
    if (comma)
    {
        out << ",";
    }
}

void JsonDouble(std::ostringstream& out, const char* name, double value, bool comma = true)
{
    out << "\"" << name << "\":" << value;
    if (comma)
    {
        out << ",";
    }
}

void JsonBool(std::ostringstream& out, const char* name, bool value, bool comma = true)
{
    out << "\"" << name << "\":" << (value ? "true" : "false");
    if (comma)
    {
        out << ",";
    }
}

std::string ExtractJsonString(const std::string& json, const std::string& name)
{
    const std::string marker = "\"" + name + "\"";
    auto pos = json.find(marker);
    if (pos == std::string::npos)
    {
        return std::string();
    }

    pos = json.find(':', pos + marker.size());
    if (pos == std::string::npos)
    {
        return std::string();
    }

    pos = json.find('"', pos + 1);
    if (pos == std::string::npos)
    {
        return std::string();
    }

    std::string result;
    bool escaped = false;

    for (size_t i = pos + 1; i < json.size(); ++i)
    {
        const char ch = json[i];
        if (escaped)
        {
            switch (ch)
            {
            case 'n':
                result.push_back('\n');
                break;
            case 'r':
                result.push_back('\r');
                break;
            case 't':
                result.push_back('\t');
                break;
            default:
                result.push_back(ch);
                break;
            }
            escaped = false;
        }
        else if (ch == '\\')
        {
            escaped = true;
        }
        else if (ch == '"')
        {
            break;
        }
        else
        {
            result.push_back(ch);
        }
    }

    return result;
}

int ParseInt(const std::string& value)
{
    try
    {
        return std::stoi(value);
    }
    catch (...)
    {
        return 0;
    }
}

POINT ParsePoint(const std::string& value)
{
    POINT point = {};
    GetCursorPos(&point);

    const auto comma = value.find(',');
    if (comma == std::string::npos)
    {
        return point;
    }

    point.x = ParseInt(value.substr(0, comma));
    point.y = ParseInt(value.substr(comma + 1));
    return point;
}

struct MoveWindowSearch
{
    DWORD processId;
    POINT point;
    const char* titlePart;
    bool moved;
};

BOOL CALLBACK MoveMatchingWindowProc(HWND hwnd, LPARAM lParam)
{
    auto* search = reinterpret_cast<MoveWindowSearch*>(lParam);
    if (search->moved || !IsWindowVisible(hwnd))
    {
        return TRUE;
    }

    DWORD windowProcessId = 0;
    GetWindowThreadProcessId(hwnd, &windowProcessId);
    if (windowProcessId != search->processId)
    {
        return TRUE;
    }

    char title[256] = {};
    GetWindowTextA(hwnd, title, static_cast<int>(sizeof(title)));
    if (search->titlePart != nullptr && strstr(title, search->titlePart) == nullptr)
    {
        return TRUE;
    }

    RECT rect = {};
    if (!GetWindowRect(hwnd, &rect))
    {
        return TRUE;
    }

    SetWindowPos(
        hwnd,
        HWND_TOP,
        search->point.x,
        search->point.y,
        rect.right - rect.left,
        rect.bottom - rect.top,
        SWP_SHOWWINDOW);
    search->moved = true;
    return FALSE;
}

void MoveMatchingWindowNearPoint(const char* titlePart, POINT point)
{
    MoveWindowSearch search = {};
    search.processId = GetCurrentProcessId();
    search.point = point;
    search.titlePart = titlePart;
    search.moved = false;
    EnumWindows(MoveMatchingWindowProc, reinterpret_cast<LPARAM>(&search));
}
} // namespace

HMODULE g_moduleHandle = nullptr;

OzStripsRadarScreen::OzStripsRadarScreen(OzStripsEuroScopePlugin* owner)
    : owner_(owner)
{
}

void OzStripsRadarScreen::StartBaseFunction(const std::string& callsign, int itemCode, const std::string& itemString, int functionId, POINT point)
{
    RECT area = {point.x - 20, point.y - 10, point.x + 20, point.y + 10};
    StartTagFunction(callsign.c_str(), nullptr, itemCode, itemString.c_str(), nullptr, functionId, point, area);
}

void OzStripsRadarScreen::OnAsrContentToBeClosed()
{
    if (owner_ != nullptr)
    {
        owner_->ForgetRadarScreen(this);
        owner_ = nullptr;
    }

    delete this;
}

OzStripsEuroScopePlugin::OzStripsEuroScopePlugin()
    : CPlugIn(COMPATIBILITY_CODE, kPluginName, kPluginVersion, kAuthor, kCopyright),
      helperProcess_(nullptr),
      helperStarted_(false),
      lastSnapshotCounter_(-1)
{
    bridge_.Start(FullPipeName());
    DisplayUserMessage(kPluginName, "OzStrips", "Loaded. Type .ozstrips to open the stripboard.", true, true, false, false, false);
}

OzStripsEuroScopePlugin::~OzStripsEuroScopePlugin()
{
    ShutdownHelper();
    bridge_.Stop();
    ResetHelperProcess();
}

bool OzStripsEuroScopePlugin::OnCompileCommand(const char* commandLine)
{
    const auto command = ToLower(Trim(Safe(commandLine)));
    if (command == ".ozstrips" || command == ".ozstrips open")
    {
        LaunchHelper();
        SendSnapshot();
        return true;
    }

    if (command == ".ozstrips snapshot")
    {
        SendSnapshot();
        DisplayUserMessage(kPluginName, "OzStrips", "Snapshot sent to helper.", false, true, false, false, false);
        return true;
    }

    return false;
}

CRadarScreen* OzStripsEuroScopePlugin::OnRadarScreenCreated(const char*, bool, bool, bool, bool)
{
    auto screen = new OzStripsRadarScreen(this);
    radarScreens_.push_back(screen);
    return screen;
}

void OzStripsEuroScopePlugin::OnTimer(int counter)
{
    DrainCommands();

    if (counter == lastSnapshotCounter_)
    {
        return;
    }

    if (bridge_.IsConnected() && counter % 3 == 0)
    {
        SendSnapshot();
        lastSnapshotCounter_ = counter;
    }
}

void OzStripsEuroScopePlugin::OnFlightPlanFlightPlanDataUpdate(CFlightPlan flightPlan)
{
    SendSingleFlight(flightPlan, "flightPlanData");
}

void OzStripsEuroScopePlugin::OnFlightPlanControllerAssignedDataUpdate(CFlightPlan flightPlan, int)
{
    SendSingleFlight(flightPlan, "controllerAssignedData");
}

void OzStripsEuroScopePlugin::ForgetRadarScreen(OzStripsRadarScreen* screen)
{
    radarScreens_.erase(std::remove(radarScreens_.begin(), radarScreens_.end(), screen), radarScreens_.end());
}

void OzStripsEuroScopePlugin::LaunchHelper()
{
    const auto helper = HelperPath();
    if (helper.empty())
    {
        DisplayUserMessage(kPluginName, "OzStrips", "Could not find OzStripsEuroScope.Helper.exe next to the plugin DLL.", true, true, false, true, false);
        return;
    }

    if (IsHelperRunning())
    {
        bridge_.SendLine("{\"type\":\"focus\"}");
        SendSnapshot();
        return;
    }

    ResetHelperProcess();

    std::ostringstream command;
    command << "\"" << helper << "\" --pipe \"" << PipeNameOnly() << "\" --plugin-pid " << GetCurrentProcessId();

    STARTUPINFOA startup = {};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process = {};

    auto commandLine = command.str();
    const BOOL created = CreateProcessA(
        nullptr,
        &commandLine[0],
        nullptr,
        nullptr,
        FALSE,
        0,
        nullptr,
        ModuleDirectory().c_str(),
        &startup,
        &process);

    if (!created)
    {
        DisplayUserMessage(kPluginName, "OzStrips", "Failed to launch OzStrips helper.", true, true, false, true, false);
        return;
    }

    CloseHandle(process.hThread);
    helperProcess_ = process.hProcess;
    helperStarted_ = true;
}

bool OzStripsEuroScopePlugin::IsHelperRunning()
{
    if (!helperStarted_ || helperProcess_ == nullptr)
    {
        return false;
    }

    const auto waitResult = WaitForSingleObject(helperProcess_, 0);
    return waitResult == WAIT_TIMEOUT;
}

void OzStripsEuroScopePlugin::ShutdownHelper()
{
    if (!IsHelperRunning())
    {
        return;
    }

    bridge_.SendLine("{\"type\":\"shutdown\"}");
    Sleep(250);

    if (WaitForSingleObject(helperProcess_, 2000) == WAIT_TIMEOUT)
    {
        TerminateProcess(helperProcess_, 0);
        WaitForSingleObject(helperProcess_, 1000);
    }
}

void OzStripsEuroScopePlugin::ResetHelperProcess()
{
    if (helperProcess_ != nullptr)
    {
        CloseHandle(helperProcess_);
        helperProcess_ = nullptr;
    }

    helperStarted_ = false;
}

void OzStripsEuroScopePlugin::SendSnapshot()
{
    bridge_.SendLine(BuildSnapshotJson());
}

void OzStripsEuroScopePlugin::SendSingleFlight(CFlightPlan flightPlan, const char* reason)
{
    if (!bridge_.IsConnected() || !flightPlan.IsValid())
    {
        return;
    }

    std::ostringstream out;
    out << "{\"type\":\"flightChanged\",";
    JsonString(out, "reason", reason == nullptr ? std::string() : std::string(reason));
    out << "\"flight\":" << BuildFlightJson(flightPlan, false) << "}";
    bridge_.SendLine(out.str());
}

void OzStripsEuroScopePlugin::DrainCommands()
{
    for (const auto& command : bridge_.TakeCommands())
    {
        ApplyCommand(command);
    }
}

void OzStripsEuroScopePlugin::ApplyCommand(const std::string& command)
{
    const auto action = ExtractJsonString(command, "command");
    const auto callsign = ExtractJsonString(command, "callsign");
    const auto value = ExtractJsonString(command, "value");

    if (callsign.empty())
    {
        return;
    }

    auto flightPlan = FindFlightPlanByCallsign(callsign);
    if (!flightPlan.IsValid())
    {
        return;
    }

    auto assigned = flightPlan.GetControllerAssignedData();
    auto data = flightPlan.GetFlightPlanData();

    if (action == "SetScratchpad")
    {
        assigned.SetScratchPadString(value.c_str());
        SendSingleFlight(flightPlan, "command");
    }
    else if (action == "SetCfl")
    {
        assigned.SetClearedAltitude(ParseInt(value));
        SendSingleFlight(flightPlan, "command");
    }
    else if (action == "SetFinalAltitude")
    {
        data.SetFinalAltitude(ParseInt(value));
        data.AmendFlightPlan();
        SendSingleFlight(flightPlan, "command");
    }
    else if (action == "SetSquawk")
    {
        assigned.SetSquawk(value.c_str());
        SendSingleFlight(flightPlan, "command");
    }
    else if (action == "SetRoute")
    {
        data.SetRoute(value.c_str());
        data.AmendFlightPlan();
        SendSingleFlight(flightPlan, "command");
    }
    else if (action == "SetAssignedHeading")
    {
        assigned.SetAssignedHeading(ParseInt(value));
        SendSingleFlight(flightPlan, "command");
    }
    else if (action == "OpenFlightPlan")
    {
        StartBaseTagFunction(flightPlan, TAG_ITEM_TYPE_CALLSIGN, callsign, TAG_ITEM_FUNCTION_OPEN_FP_DIALOG, value);
    }
    else if (action == "OpenSidMenu")
    {
        StartBaseTagFunction(flightPlan, TAG_ITEM_TYPE_ASSIGNED_SID, Safe(data.GetSidName()), TAG_ITEM_FUNCTION_ASSIGNED_SID, value);
    }
    else if (action == "OpenRunwayMenu")
    {
        StartBaseTagFunction(flightPlan, TAG_ITEM_TYPE_ASSIGNED_RUNWAY, Safe(data.GetDepartureRwy()), TAG_ITEM_FUNCTION_ASSIGNED_RUNWAY, value);
    }
    else if (action == "ShowMessage")
    {
        DisplayUserMessage(kPluginName, "OzStrips", value.c_str(), true, true, false, false, false);
    }
}

CFlightPlan OzStripsEuroScopePlugin::FindFlightPlanByCallsign(const std::string& callsign) const
{
    auto current = FlightPlanSelectFirst();
    while (current.IsValid())
    {
        if (_stricmp(current.GetCallsign(), callsign.c_str()) == 0)
        {
            return current;
        }

        current = FlightPlanSelectNext(current);
    }

    return CFlightPlan();
}

OzStripsRadarScreen* OzStripsEuroScopePlugin::ActiveRadarScreen() const
{
    return radarScreens_.empty() ? nullptr : radarScreens_.back();
}

bool OzStripsEuroScopePlugin::StartBaseTagFunction(CFlightPlan flightPlan, int itemCode, const std::string& itemString, int functionId, const std::string& pointValue)
{
    auto screen = ActiveRadarScreen();
    if (screen == nullptr)
    {
        DisplayUserMessage(kPluginName, "OzStrips", "Open a EuroScope radar screen before using this OzStrips action.", true, true, false, true, false);
        return false;
    }

    SetASELAircraft(flightPlan);
    const auto point = ParsePoint(pointValue);
    SetCursorPos(point.x, point.y);
    screen->StartBaseFunction(Safe(flightPlan.GetCallsign()), itemCode, itemString, functionId, point);
    if (functionId == TAG_ITEM_FUNCTION_OPEN_FP_DIALOG)
    {
        Sleep(80);
        MoveMatchingWindowNearPoint("Flight Plan", point);
    }

    return true;
}

std::string OzStripsEuroScopePlugin::BuildSnapshotJson()
{
    std::ostringstream out;
    out << "{\"type\":\"snapshot\",";
    JsonString(out, "pluginVersion", kPluginVersion);
    JsonInt(out, "connectionType", GetConnectionType());

    auto controller = ControllerMyself();
    out << "\"controller\":{";
    JsonString(out, "callsign", controller.IsValid() ? Safe(controller.GetCallsign()) : std::string());
    JsonBool(out, "isController", controller.IsValid(), false);
    out << "},";

    auto selected = FlightPlanSelectASEL();
    const auto selectedCallsign = selected.IsValid() ? Safe(selected.GetCallsign()) : std::string();

    out << "\"flights\":[";
    bool first = true;
    auto current = FlightPlanSelectFirst();
    while (current.IsValid())
    {
        if (!first)
        {
            out << ",";
        }

        first = false;
        out << BuildFlightJson(current, selectedCallsign == Safe(current.GetCallsign()));
        current = FlightPlanSelectNext(current);
    }

    out << "]}";
    return out.str();
}

std::string OzStripsEuroScopePlugin::BuildFlightJson(CFlightPlan flightPlan, bool selected)
{
    auto data = flightPlan.GetFlightPlanData();
    auto assigned = flightPlan.GetControllerAssignedData();
    auto extractedRoute = flightPlan.GetExtractedRoute();
    const auto rflFeet = data.GetFinalAltitude();
    const auto assignedCflFeet = assigned.GetClearedAltitude();
    const auto cflFeet = assignedCflFeet > 2 && assignedCflFeet != rflFeet ? assignedCflFeet : 0;

    std::string firstWaypoint;
    if (extractedRoute.GetPointsNumber() > 0)
    {
        firstWaypoint = Safe(extractedRoute.GetPointName(0));
    }

    std::ostringstream out;
    out << "{";
    JsonString(out, "callsign", Safe(flightPlan.GetCallsign()));
    JsonString(out, "aircraftType", Safe(data.GetAircraftFPType()));
    JsonString(out, "wakeCategory", std::string(1, data.GetAircraftWtc()));
    JsonString(out, "flightRules", Safe(data.GetPlanType()));
    JsonString(out, "adep", Safe(data.GetOrigin()));
    JsonString(out, "ades", Safe(data.GetDestination()));
    JsonString(out, "route", Safe(data.GetRoute()));
    JsonString(out, "sid", Safe(data.GetSidName()));
    JsonString(out, "star", Safe(data.GetStarName()));
    JsonString(out, "runway", Safe(data.GetDepartureRwy()));
    JsonString(out, "arrivalRunway", Safe(data.GetArrivalRwy()));
    JsonString(out, "activeDepartureRunway", FindActiveDepartureRunway(Safe(data.GetOrigin())));
    JsonString(out, "etd", Safe(data.GetEstimatedDepartureTime()));
    JsonString(out, "firstWaypoint", firstWaypoint);
    JsonString(out, "squawk", Safe(assigned.GetSquawk()));
    JsonString(out, "scratchpad", Safe(assigned.GetScratchPadString()));
    JsonString(out, "groundState", Safe(flightPlan.GetGroundState()));
    JsonInt(out, "rflFeet", rflFeet);
    JsonInt(out, "cflFeet", cflFeet);
    JsonInt(out, "assignedHeading", assigned.GetAssignedHeading());
    out << "\"routePoints\":[";
    for (int i = 0; i < extractedRoute.GetPointsNumber(); ++i)
    {
        if (i > 0)
        {
            out << ",";
        }

        const auto position = extractedRoute.GetPointPosition(i);
        out << "{";
        JsonString(out, "name", Safe(extractedRoute.GetPointName(i)));
        JsonDouble(out, "latitude", position.m_Latitude);
        JsonDouble(out, "longitude", position.m_Longitude, false);
        out << "}";
    }

    out << "],";
    out << BuildSidOptionsJson(Safe(data.GetOrigin())) << ",";
    JsonBool(out, "selected", selected, false);
    out << "}";
    return out.str();
}

std::string OzStripsEuroScopePlugin::BuildSidOptionsJson(const std::string& origin)
{
    std::ostringstream out;
    std::set<std::string> seen;
    bool first = true;

    out << "\"sidOptions\":[";

    const auto sectorOptions = LoadSectorSidOptions();
    for (const auto& sid : sectorOptions)
    {
        const auto key = ToUpper(sid.airport + "|" + sid.runway + "|" + sid.name);

        if (!sid.name.empty() && AirportMatchesSidElement(sid.airport, origin) && seen.insert(key).second)
        {
            if (!first)
            {
                out << ",";
            }

            first = false;
            out << "{";
            JsonString(out, "name", sid.name);
            JsonString(out, "runway", sid.runway, false);
            out << "}";
        }
    }

    if (first)
    {
        SelectActiveSectorfile();
        auto sid = SectorFileElementSelectFirst(SECTOR_ELEMENT_SID);
        while (sid.IsValid())
        {
            const auto name = Trim(Safe(sid.GetName()));
            const auto runway = Trim(Safe(sid.GetRunwayName(0)));
            const auto airport = Trim(Safe(sid.GetAirportName()));
            const auto key = ToUpper(airport + "|" + runway + "|" + name);

            if (!name.empty() && AirportMatchesSidElement(airport, origin) && seen.insert(key).second)
            {
                if (!first)
                {
                    out << ",";
                }

                first = false;
                out << "{";
                JsonString(out, "name", name);
                JsonString(out, "runway", runway, false);
                out << "}";
            }

            sid = SectorFileElementSelectNext(sid, SECTOR_ELEMENT_SID);
        }
    }

    out << "]";
    return out.str();
}

std::string OzStripsEuroScopePlugin::FindActiveDepartureRunway(const std::string& origin)
{
    SelectActiveSectorfile();
    auto runwayElement = SectorFileElementSelectFirst(SECTOR_ELEMENT_RUNWAY);
    while (runwayElement.IsValid())
    {
        const auto airport = Trim(Safe(runwayElement.GetAirportName()));
        if (AirportMatchesSidElement(airport, origin))
        {
            for (int index = 0; index < 2; ++index)
            {
                const auto runway = Trim(Safe(runwayElement.GetRunwayName(index)));
                if (!runway.empty() && runwayElement.IsElementActive(true, index))
                {
                    return runway;
                }
            }
        }

        runwayElement = SectorFileElementSelectNext(runwayElement, SECTOR_ELEMENT_RUNWAY);
    }

    return std::string();
}

std::vector<OzStripsEuroScopePlugin::SidOption> OzStripsEuroScopePlugin::LoadSectorSidOptions()
{
    std::vector<SidOption> options;
    const auto path = ResolveSectorFilePath();
    if (path.empty())
    {
        return options;
    }

    std::ifstream file(path);
    if (!file.good())
    {
        return options;
    }

    std::string line;
    while (std::getline(file, line))
    {
        line = Trim(line);
        if (line.empty() || line[0] == ';' || !StartsWithIgnoreCase(line, "SID:"))
        {
            continue;
        }

        auto parts = SplitSidLine(line);
        if (parts.size() < 4)
        {
            continue;
        }

        options.push_back(SidOption
        {
            ToUpper(parts[1]),
            ToUpper(parts[2]),
            parts[3],
        });
    }

    return options;
}

std::string OzStripsEuroScopePlugin::ResolveSectorFilePath() const
{
    auto controller = ControllerMyself();
    const auto sectorName = controller.IsValid() ? Trim(Safe(controller.GetSectorFileName())) : std::string();
    if (sectorName.empty())
    {
        return std::string();
    }

    std::string resolved;
    if (TrySectorPath(sectorName, resolved))
    {
        return resolved;
    }

    char currentDirectory[MAX_PATH] = {};
    GetCurrentDirectoryA(MAX_PATH, currentDirectory);

    const auto moduleDir = ModuleDirectory();
    const auto parent = ParentDirectory(moduleDir);
    const auto grandParent = ParentDirectory(parent);
    const auto fileName = FileNameOnly(sectorName);
    const auto eseFileName = FileNameOnly(WithEseExtension(sectorName));
    const std::vector<std::string> roots =
    {
        Safe(currentDirectory),
        moduleDir,
        parent,
        grandParent,
    };

    for (const auto& root : roots)
    {
        if (root.empty())
        {
            continue;
        }

        const auto direct = root + "\\" + sectorName;
        if (TrySectorPath(direct, resolved))
        {
            return resolved;
        }

        const auto byName = root + "\\" + fileName;
        if (TrySectorPath(byName, resolved))
        {
            return resolved;
        }
    }

    std::vector<std::string> targetNames;
    targetNames.push_back(eseFileName);
    targetNames.push_back(fileName);

    const auto programFilesX86 = GetEnvironmentString("ProgramFiles(x86)");
    if (!programFilesX86.empty())
    {
        const auto found = FindFileRecursive(JoinPath(JoinPath(programFilesX86, "EuroScope"), "VATNZ-SKYLINE_2412"), targetNames, 2);
        if (!found.empty())
        {
            return found;
        }

        const auto anyEuroScopeProfile = FindFileRecursive(JoinPath(programFilesX86, "EuroScope"), targetNames, 4);
        if (!anyEuroScopeProfile.empty())
        {
            return anyEuroScopeProfile;
        }
    }

    const auto programFiles = GetEnvironmentString("ProgramFiles");
    if (!programFiles.empty())
    {
        const auto anyEuroScopeProfile = FindFileRecursive(JoinPath(programFiles, "EuroScope"), targetNames, 4);
        if (!anyEuroScopeProfile.empty())
        {
            return anyEuroScopeProfile;
        }
    }

    return std::string();
}

std::string OzStripsEuroScopePlugin::ModuleDirectory() const
{
    char modulePath[MAX_PATH] = {};
    GetModuleFileNameA(g_moduleHandle, modulePath, MAX_PATH);
    std::string path(modulePath);
    const auto slash = path.find_last_of("\\/");
    return slash == std::string::npos ? std::string(".") : path.substr(0, slash);
}

std::string OzStripsEuroScopePlugin::HelperPath() const
{
    const auto moduleDir = ModuleDirectory();
    auto helper = moduleDir + "\\OzStripsEuroScope.Helper.exe";

    DWORD attrs = GetFileAttributesA(helper.c_str());
    if (attrs != INVALID_FILE_ATTRIBUTES && (attrs & FILE_ATTRIBUTE_DIRECTORY) == 0)
    {
        return helper;
    }

    helper = moduleDir + "\\OzStripsEuroScope.Helper\\OzStripsEuroScope.Helper.exe";
    attrs = GetFileAttributesA(helper.c_str());
    if (attrs != INVALID_FILE_ATTRIBUTES && (attrs & FILE_ATTRIBUTE_DIRECTORY) == 0)
    {
        return helper;
    }

    return std::string();
}

std::string OzStripsEuroScopePlugin::PipeNameOnly() const
{
    return "OzStripsEuroScope-" + std::to_string(GetCurrentProcessId());
}

std::string OzStripsEuroScopePlugin::FullPipeName() const
{
    return "\\\\.\\pipe\\" + PipeNameOnly();
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_moduleHandle = module;
    }

    return TRUE;
}

void __declspec(dllexport) EuroScopePlugInInit(CPlugIn** pluginInstance)
{
    *pluginInstance = g_plugin = new OzStripsEuroScopePlugin();
}

void __declspec(dllexport) EuroScopePlugInExit()
{
    delete g_plugin;
    g_plugin = nullptr;
}
