local policy = Import("overhauledlobby/restrictions.lua")
if policy.Enabled then
    -- A proxy preserves existing availability (including other selected mods),
    -- while allowing templates loaded later to be checked against final Tags.
    local original = AvailableUnits
    AvailableUnits = setmetatable({}, {
        __index = function(_,id)
            if policy.Denied(id) then return false end
            return original[id]
        end,
        __newindex = function(_,id,value) original[id] = value end,
        -- Installed Lua51 enables __pairs. Preserve original iteration while
        -- returning the same effective availability as indexed reads.
        __pairs = function()
            local iterator,state,first = pairs(original)
            return function(_,previous)
                local id,value = iterator(state,previous)
                if id ~= nil and policy.Denied(id) then value = false end
                return id,value
            end,nil,first
        end
    })
end
