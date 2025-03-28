## RMI.LeadCallProxyAPI

This service is a robust RESTful API service to validate incoming phone calls. It featured automated failsafes to ensure high availability and reliability. In the event of third-party dependency failures, the service would seamlessly switch to a backup dependency or bypass the core functionality to maintain operational continuity. Additionally, the application generated detailed error notifications to Slack for any unhandled exceptions, enabling rapid issue resolution. All call data was stored for analysis and reporting, providing valuable insights to improve processes and decision-making.

