#pragma once

#include <string>
#include <vector>
#include <windows.h>

#include "EuroScopePlugIn.h"
#include "PipeBridge.h"

extern HMODULE g_moduleHandle;

class OzStripsEuroScopePlugin;

class OzStripsRadarScreen : public EuroScopePlugIn::CRadarScreen
{
public:
    explicit OzStripsRadarScreen(OzStripsEuroScopePlugin* owner);

    void StartBaseFunction(const std::string& callsign, int itemCode, const std::string& itemString, int functionId, POINT point);
    void OnAsrContentToBeClosed() override;

private:
    OzStripsEuroScopePlugin* owner_;
};

class OzStripsEuroScopePlugin : public EuroScopePlugIn::CPlugIn
{
public:
    OzStripsEuroScopePlugin();
    ~OzStripsEuroScopePlugin() override;

    bool OnCompileCommand(const char* commandLine) override;
    EuroScopePlugIn::CRadarScreen* OnRadarScreenCreated(const char* displayName, bool needRadarContent, bool geoReferenced, bool canBeSaved, bool canBeCreated) override;
    void OnTimer(int counter) override;
    void OnFlightPlanFlightPlanDataUpdate(EuroScopePlugIn::CFlightPlan flightPlan) override;
    void OnFlightPlanControllerAssignedDataUpdate(EuroScopePlugIn::CFlightPlan flightPlan, int dataType) override;
    void ForgetRadarScreen(OzStripsRadarScreen* screen);

private:
    struct SidOption
    {
        std::string airport;
        std::string runway;
        std::string name;
    };

    void LaunchHelper();
    bool IsHelperRunning();
    void ShutdownHelper();
    void ResetHelperProcess();
    void SendSnapshot();
    void SendSingleFlight(EuroScopePlugIn::CFlightPlan flightPlan, const char* reason);
    void DrainCommands();
    void ApplyCommand(const std::string& command);
    EuroScopePlugIn::CFlightPlan FindFlightPlanByCallsign(const std::string& callsign) const;
    OzStripsRadarScreen* ActiveRadarScreen() const;
    bool StartBaseTagFunction(EuroScopePlugIn::CFlightPlan flightPlan, int itemCode, const std::string& itemString, int functionId, const std::string& pointValue);

    std::string BuildSnapshotJson();
    std::string BuildFlightJson(EuroScopePlugIn::CFlightPlan flightPlan, bool selected);
    std::string BuildSidOptionsJson(const std::string& origin);
    std::string FindActiveDepartureRunway(const std::string& origin);
    std::vector<SidOption> LoadSectorSidOptions();
    std::string ResolveSectorFilePath() const;
    std::string ModuleDirectory() const;
    std::string HelperPath() const;
    std::string PipeNameOnly() const;
    std::string FullPipeName() const;

    PipeBridge bridge_;
    std::vector<OzStripsRadarScreen*> radarScreens_;
    HANDLE helperProcess_;
    bool helperStarted_;
    int lastSnapshotCounter_;
};
