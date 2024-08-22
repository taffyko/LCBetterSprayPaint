using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Netcode;

interface INetVar : IDisposable {
    void Synchronize();

    /// Return all INetVar instances on a given object
    static INetVar[] GetAllNetVars(object self) {
        return self.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(field => typeof(INetVar).IsAssignableFrom(field.FieldType))
            .Select(field => (INetVar)field.GetValue(self)).ToArray();
    }
}

class NetVar<T> : INetVar where T : IEquatable<T> {
    readonly public NetworkVariable<T> networkVariable;

    bool deferPending = true;
    T deferredValue;

    // Always in sync with networkVariable.Value
    // The only time this will ever hold a different value is when this client is the owner
    // and the value has just been set and not yet made a round-trip back from the server
    private T localValue {
        set {
            if (isGlued) { setGlued!(value); }
            _localValue = value;
        }
        get {
            return _localValue;
        }
    }

    private T _localValue;

    // Getter/setter to synchronize value with an external local variable
    readonly bool isGlued;
    readonly Action<T>? setGlued;
    readonly Func<T>? getGlued;

    readonly Func<T, T>? validate;
    /// Supply an implementation that informs the variable whether the current client has authority over this variable right now
    readonly Func<bool> inControl;
    readonly Action<T> SetOnServer;
    readonly NetworkVariable<T>.OnValueChangedDelegate? onChange;
    public NetVar(
        /// Unity requires that the underlying NetworkVariable be statically exposed via a field declaration on the NGO type.
        /// Supply a field reference, NetVar will populate it with its underlying NetworkVariable instance.
        out NetworkVariable<T> networkVariable,
        /// Supply a ServerRpc implementation used to update the underlying NetworkVariable
        Action<T> SetOnServer,
        Func<bool> inControl,
        T initialValue = default!,
        NetworkVariable<T>.OnValueChangedDelegate? onChange = null,
        Action<T>? setGlued = null,
        Func<T>? getGlued = null,
        Func<T, T>? validate = null
    ) {
        isGlued = setGlued != null && getGlued != null;
        this.setGlued = setGlued;
        this.getGlued = getGlued;

        this.validate = validate;
        this.onChange = onChange;
        this.SetOnServer = SetOnServer;
        this.inControl = inControl;

        this.networkVariable = new NetworkVariable<T>(initialValue);
        networkVariable = this.networkVariable;
        _localValue = initialValue;
        localValue = initialValue;
        deferredValue = initialValue;

        networkVariable.OnValueChanged += (prevValue, currentValue) => {
            if (!EqualityComparer<T>.Default.Equals(localValue, currentValue)) {
                Synchronize();
                OnChange(prevValue, currentValue);
            }
        };
    }

    public void ServerSet(T value) {
        if (validate != null) { value = validate(value); }
        networkVariable.Value = value;
    }

    public void SetDeferred(T value) {
        deferredValue = value;
    }

    void OnChange(T prevValue, T currentValue) {
        if (onChange != null) { onChange(prevValue, currentValue); }
    }

    public T Value {
        set {
            if (inControl()) {
                deferPending = false;
                if (validate != null) { value = validate(value); }
                if (!EqualityComparer<T>.Default.Equals(localValue, value)) {
                    T prevValue = localValue;
                    localValue = value;
                    SetOnServer(value);
                    OnChange(prevValue, value);
                }
            }
        }
        get {
            Synchronize();
            return localValue;
        }
    }

    public void UpdateDeferred() {
        if (inControl()) {
            if (deferPending) {
                Value = deferredValue;
            }
        }
        deferPending = false;
    }

    // Should be called every tick
    public void Synchronize() {
        if (!inControl()) {
            localValue = networkVariable.Value;
            deferPending = false;
        } else if (isGlued) {
            Value = getGlued!();
        } else {
            Value = localValue; // trigger validation constraints
        }
    }

    public void Dispose()
    {
        networkVariable.Dispose();
    }
}
