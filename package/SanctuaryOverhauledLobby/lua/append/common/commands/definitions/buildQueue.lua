local policy = Import("overhauledlobby/restrictions.lua")
if policy.Enabled then
    local accept = RequestAddToBuildQueue.Receive
    RequestAddToBuildQueue.Receive = function(request)
        if not request or not policy.Denied(request.tpId) then return accept(request) end
        -- Complete prediction and send the authoritative unchanged queue.
        for _,id in ipairs(request.unitsIDs or {}) do
            local builder = GetUnitById(id)
            if builder then
                OnPendingQueueOperationCompleted.Send(id)
                builder:SendUpdateBuildQueueCommand()
            end
        end
    end
end
