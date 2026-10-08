import tempfile
import unittest
from pathlib import Path

from werkzeug.security import generate_password_hash

from app import create_app, insert_row


class CrmApiTests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.TemporaryDirectory()
        self.database_path = str(Path(self.temp_dir.name) / "test.sqlite")
        self.app = create_app(
            {
                "TESTING": True,
                "DATABASE": self.database_path,
                "JWT_SECRET": "this-is-a-test-secret-longer-than-32-chars",
                "SESSION_COOKIE_SECURE": False,
            }
        )
        with self.app.app_context():
            self.admin = insert_row(
                "users",
                {
                    "fullName": "Test Admin",
                    "email": "admin@test.local",
                    "passwordHash": generate_password_hash("Admin@Test123"),
                    "role": "Admin",
                },
            )
            self.sales = insert_row(
                "users",
                {
                    "fullName": "Test Sales",
                    "email": "sales@test.local",
                    "passwordHash": generate_password_hash("Sales@Test123"),
                    "role": "SalesExecutive",
                },
            )
        self.client = self.app.test_client()

    def tearDown(self):
        self.temp_dir.cleanup()

    def test_health_frontend_and_api_docs(self):
        self.assertEqual(self.client.get("/api/health").json, {"status": "ok", "database": "sqlite"})
        response = self.client.get("/")
        self.assertIn(b"AcxiomCRM", response.data)
        response.close()
        response = self.client.get("/images/acxiom-mark.svg")
        self.assertEqual(response.status_code, 200)
        self.assertIn(b"<svg", response.data)
        response.close()
        self.assertEqual(self.client.get("/api/docs").status_code, 200)
        self.assertEqual(self.client.get("/swagger").status_code, 200)

    def test_auth_crud_dashboard_reporting_and_conversion(self):
        response = self.client.post(
            "/api/auth/login",
            json={"email": "admin@test.local", "password": "Admin@Test123"},
        )
        self.assertEqual(response.status_code, 200)

        response = self.client.post(
            "/api/customers",
            json={
                "customerName": "Test Customer",
                "email": "customer@example.test",
                "phone": "9876543210",
                "companyName": "Test Co",
                "assignedTo": self.sales["id"],
            },
        )
        self.assertEqual(response.status_code, 201, response.json)
        customer = response.json["item"]

        response = self.client.post(
            "/api/leads",
            json={
                "leadName": "Test Lead",
                "email": "lead@example.test",
                "phone": "9876543211",
                "companyName": "Lead Co",
                "source": "Referral",
                "expectedValue": 5000,
                "assignedTo": self.sales["id"],
            },
        )
        self.assertEqual(response.status_code, 201, response.json)
        lead = response.json["item"]
        response = self.client.put(
            f"/api/leads/{lead['id']}",
            json={
                "leadName": lead["leadName"],
                "email": lead["email"],
                "phone": lead["phone"],
                "companyName": lead["companyName"],
                "source": lead["source"],
                "status": "Qualified",
                "priority": lead["priority"],
                "expectedValue": lead["expectedValue"],
                "assignedTo": self.sales["id"],
            },
        )
        self.assertEqual(response.status_code, 200, response.json)

        response = self.client.post(
            "/api/opportunities",
            json={
                "opportunityName": "Test opportunity",
                "customerId": customer["id"],
                "leadId": lead["id"],
                "amount": 5000,
                "probability": 40,
                "stage": "Qualification",
                "expectedCloseDate": "2030-12-31",
                "status": "Open",
                "assignedTo": self.sales["id"],
            },
        )
        self.assertEqual(response.status_code, 201, response.json)

        response = self.client.post(
            "/api/followups",
            json={
                "customerId": customer["id"],
                "subject": "Call customer",
                "followUpType": "Call",
                "followUpDate": "2030-12-31",
                "status": "Planned",
                "assignedTo": self.sales["id"],
            },
        )
        self.assertEqual(response.status_code, 201, response.json)

        self.assertEqual(self.client.get("/api/dashboard").json["totalCustomers"], 1)
        self.assertEqual(self.client.get("/api/reports/pipeline").json["totalAmount"], 5000)
        response = self.client.post(f"/api/leads/{lead['id']}/convert", json={})
        self.assertEqual(response.status_code, 201, response.json)
        self.assertEqual(self.client.get("/api/reports/conversion").json["converted"], 1)

        self.client.post(
            "/api/auth/logout",
        )
        response = self.client.post(
            "/api/auth/login",
            json={"email": "sales@test.local", "password": "Sales@Test123"},
        )
        self.assertEqual(response.status_code, 200)
        self.assertEqual(self.client.get("/api/customers").json["totalCount"], 2)


if __name__ == "__main__":
    unittest.main()
