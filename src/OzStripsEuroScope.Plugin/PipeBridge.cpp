#include "PipeBridge.h"

#include <chrono>

PipeBridge::PipeBridge()
    : running_(false),
      connected_(false),
      pipe_(INVALID_HANDLE_VALUE)
{
}

PipeBridge::~PipeBridge()
{
    Stop();
}

void PipeBridge::Start(const std::string& pipeName)
{
    if (running_)
    {
        return;
    }

    pipeName_ = pipeName;
    running_ = true;
    worker_ = std::thread(&PipeBridge::Worker, this);
}

void PipeBridge::Stop()
{
    running_ = false;
    WakePipe();
    ClosePipe();

    if (worker_.joinable())
    {
        worker_.join();
    }

    connected_ = false;
}

void PipeBridge::SendLine(const std::string& line)
{
    std::lock_guard<std::mutex> lock(mutex_);
    outbound_.push_back(line);

    while (outbound_.size() > 200)
    {
        outbound_.pop_front();
    }
}

std::vector<std::string> PipeBridge::TakeCommands()
{
    std::vector<std::string> commands;
    std::lock_guard<std::mutex> lock(mutex_);

    while (!inbound_.empty())
    {
        commands.push_back(inbound_.front());
        inbound_.pop_front();
    }

    return commands;
}

bool PipeBridge::IsConnected() const
{
    return connected_;
}

std::string PipeBridge::PipeName() const
{
    return pipeName_;
}

void PipeBridge::Worker()
{
    while (running_)
    {
        pipe_ = CreateNamedPipeA(
            pipeName_.c_str(),
            PIPE_ACCESS_DUPLEX,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
            1,
            65536,
            65536,
            1000,
            nullptr);

        if (pipe_ == INVALID_HANDLE_VALUE)
        {
            std::this_thread::sleep_for(std::chrono::seconds(1));
            continue;
        }

        const BOOL connected = ConnectNamedPipe(pipe_, nullptr) ? TRUE : (GetLastError() == ERROR_PIPE_CONNECTED);
        if (!connected)
        {
            ClosePipe();
            std::this_thread::sleep_for(std::chrono::milliseconds(250));
            continue;
        }

        connected_ = true;

        while (running_ && connected_)
        {
            if (!WriteQueuedLines())
            {
                break;
            }

            ReadAvailableCommands();
            std::this_thread::sleep_for(std::chrono::milliseconds(50));
        }

        connected_ = false;
        ClosePipe();
    }
}

void PipeBridge::ClosePipe()
{
    if (pipe_ != INVALID_HANDLE_VALUE)
    {
        FlushFileBuffers(pipe_);
        DisconnectNamedPipe(pipe_);
        CloseHandle(pipe_);
        pipe_ = INVALID_HANDLE_VALUE;
    }
}

void PipeBridge::WakePipe()
{
    if (pipeName_.empty())
    {
        return;
    }

    const HANDLE client = CreateFileA(
        pipeName_.c_str(),
        GENERIC_READ | GENERIC_WRITE,
        0,
        nullptr,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        nullptr);

    if (client != INVALID_HANDLE_VALUE)
    {
        CloseHandle(client);
    }
}

bool PipeBridge::WriteQueuedLines()
{
    std::deque<std::string> pending;
    {
        std::lock_guard<std::mutex> lock(mutex_);
        pending.swap(outbound_);
    }

    for (const auto& line : pending)
    {
        const std::string payload = line + "\n";
        DWORD written = 0;

        if (!WriteFile(pipe_, payload.data(), static_cast<DWORD>(payload.size()), &written, nullptr))
        {
            return false;
        }
    }

    return true;
}

void PipeBridge::ReadAvailableCommands()
{
    DWORD available = 0;
    if (!PeekNamedPipe(pipe_, nullptr, 0, nullptr, &available, nullptr))
    {
        connected_ = false;
        return;
    }

    if (available == 0)
    {
        return;
    }

    std::string buffer;
    buffer.resize(available);

    DWORD read = 0;
    if (!ReadFile(pipe_, &buffer[0], available, &read, nullptr))
    {
        connected_ = false;
        return;
    }

    buffer.resize(read);

    size_t start = 0;
    for (size_t i = 0; i < buffer.size(); ++i)
    {
        if (buffer[i] == '\n')
        {
            auto line = buffer.substr(start, i - start);
            if (!line.empty() && line.back() == '\r')
            {
                line.pop_back();
            }

            if (!line.empty())
            {
                std::lock_guard<std::mutex> lock(mutex_);
                inbound_.push_back(line);
            }

            start = i + 1;
        }
    }
}
