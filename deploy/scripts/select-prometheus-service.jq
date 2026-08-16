# kube-prometheus-stack's client service selects Prometheus pods via
# spec.selector.app.kubernetes.io/name, but it does not expose that value as a
# metadata label. The operator also creates a headless prometheus-operated
# service. Inspect every service and select exactly one routable Prometheus
# service; stay fail-closed if the topology is missing or ambiguous.
[
  .items[]
  | select(.spec.type == "ClusterIP")
  | select(.spec.clusterIP != "None")
  | select(.spec.selector["app.kubernetes.io/name"] == "prometheus")
  | select(any(.spec.ports[]?; .port == 9090))
] as $items
| if ($items | length) == 1
  then $items[0].metadata.name
  else error("expected exactly one non-headless Prometheus service on port 9090")
  end
