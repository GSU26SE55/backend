# kube-prometheus-stack exposes two services with the Prometheus name label:
# - a routable ClusterIP service used by clients and port-forward;
# - prometheus-operated, a headless service (clusterIP: None) for pod discovery.
# Select exactly one routable service and stay fail-closed if the topology is
# ambiguous or the client-facing service is missing.
[
  .items[]
  | select(.spec.type == "ClusterIP")
  | select(.spec.clusterIP != "None")
  | select(any(.spec.ports[]?; .port == 9090))
] as $items
| if ($items | length) == 1
  then $items[0].metadata.name
  else error("expected exactly one non-headless Prometheus service on port 9090")
  end
