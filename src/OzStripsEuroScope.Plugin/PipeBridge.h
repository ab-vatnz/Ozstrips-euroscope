#pragma once

#include <atomic>
#include <deque>
#include <mutex>
#include <string>
#include <thread>
#include <vector>
#include <windows.h>

class PipeBridge
{
public:
    PipeBridge();
    ~PipeBridge();

    PipeBridge(const PipeBridge&) = delete;
    PipeBridge& operator=(const PipeBridge&) = delete;

    void Start(const std::string& pipeName);
    void Stop();

    void SendLine(const std::string& line);
    std::vector<std::string> TakeCommands();

    bool IsConnected() const;
    std::string PipeName() const;

private:
    void Worker();
    void ClosePipe();
    void WakePipe();
    bool WriteQueuedLines();
    void ReadAvailableCommands();

    std::atomic<bool> running_;
    std::atomic<bool> connected_;
    std::string pipeName_;
    HANDLE pipe_;
    std::thread worker_;
    mutable std::mutex mutex_;
    std::deque<std::string> outbound_;
    std::deque<std::string> inbound_;
};
